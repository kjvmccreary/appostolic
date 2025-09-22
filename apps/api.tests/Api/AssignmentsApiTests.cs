using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class AssignmentsApiTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AssignmentsApiTests(WebAppFactory factory) => _factory = factory;
    // RDH Story 2 Phase A: migrate from legacy mint helper (UseTenantAsync) to real auth endpoints.
    // Pattern (mirrors MembersListTests):
    //   1. Seed a known password (matches AuthTestClientFlow.DefaultPassword)
    //   2. POST /api/auth/login to obtain neutral access token
    //   3. POST /api/auth/select-tenant for tenant-scoped access token
    // This ensures these role/membership tests exercise production-equivalent authentication.
    private const string DefaultPw = "Password123!"; // must match AuthTestClientFlow.DefaultPassword

    private async Task SeedPasswordAsync(string email, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var (hash, salt, _) = hasher.HashPassword(password);
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_memberships_requires_admin_and_returns_roles()
    {
        // Use kevin-personal where kevin is Owner
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var owner = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, owner, "kevin@example.com", "kevin-personal");

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
        }

        var ok = await owner.GetAsync($"/api/tenants/{tenantId}/memberships");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await ok.Content.ReadFromJsonAsync<List<dynamic>>();
        rows.Should().NotBeNull();

        // Create a non-admin viewer and verify 403
        string viewerEmail;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            viewerEmail = $"viewer-{Guid.NewGuid():N}@ex.com";
            var u = new User { Id = Guid.NewGuid(), Email = viewerEmail, CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = u.Id,
                Roles = Roles.None,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Seed viewer password & login with only Roles.None membership
        await SeedPasswordAsync(viewerEmail, DefaultPw);
        var viewer = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, viewer, viewerEmail, "kevin-personal"); // membership has no admin flags
        var forbidden = await viewer.GetAsync($"/api/tenants/{tenantId}/memberships");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Set_roles_replaces_flags_and_allows_noop()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var owner = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, owner, "kevin@example.com", "kevin-personal");

        Guid tenantId;
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;

            // Create a member with no flags (Viewer)
            var member = new User { Id = Guid.NewGuid(), Email = $"flags-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(member);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = member.Id,
                Roles = Roles.None,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            targetUserId = member.Id;
        }

        // Grant TenantAdmin + Creator flags
        var resp = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin", "Creator" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        payload.Should().NotBeNull();
        payload!["roles"].ToString().Should().Contain("TenantAdmin");

        // No-op change returns 204
        var noop = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin", "Creator" } });
        noop.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cannot_remove_last_admin_via_flags_returns_409()
    {
        // Create a fresh tenant where kevin is only an admin via flags (legacy role = Viewer)
        string slug = $"tenant-{Guid.NewGuid():N}";
        Guid tenantId;
        Guid kevinId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = DateTime.UtcNow };
            var kevin = await db.Users.AsNoTracking().FirstAsync(u => u.Email == "kevin@example.com");
            tenantId = tenant.Id;
            kevinId = kevin.Id;

            db.Tenants.Add(tenant);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = kevin.Id,
                Roles = Roles.TenantAdmin,     // admin via flags only
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var adminClient = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, adminClient, "kevin@example.com", slug);
        var conflict = await adminClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{kevinId}/roles", new { roles = Array.Empty<string>() });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Set_roles_returns_404_when_membership_missing_and_400_for_invalid_flag()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var owner = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, owner, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
        }

        var missing = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{Guid.NewGuid()}/roles", new { roles = new[] { "Creator" } });
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Create a new viewer and attempt invalid flag
        Guid viewerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var u = new User { Id = Guid.NewGuid(), Email = $"badflag-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = u.Id,
                Roles = Roles.None,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            viewerId = u.Id;
        }

        var bad = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{viewerId}/roles", new { roles = new[] { "NotARealFlag" } });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_admin_cannot_set_roles()
    {
        // Arrange tenant and two members: admin (kevin) and viewer
        Guid tenantId;
        Guid viewerId;
        string viewerEmail;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            viewerEmail = $"viewer2-{Guid.NewGuid():N}@ex.com";
            var viewer = new User { Id = Guid.NewGuid(), Email = viewerEmail, CreatedAt = DateTime.UtcNow };
            db.Users.Add(viewer);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = viewer.Id,
                Roles = Roles.None,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            viewerId = viewer.Id;
        }

        await SeedPasswordAsync(viewerEmail, DefaultPw);
        var viewerClient = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, viewerClient, viewerEmail, "kevin-personal"); // viewer has no admin roles
        var resp = await viewerClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{viewerId}/roles", new { roles = new[] { "Creator" } });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
