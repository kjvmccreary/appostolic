using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // Auth test real login + tenant selection flow

namespace Appostolic.Api.Tests.Api;

// Tests for the dev-only /api/dev/grant-roles endpoint
public class DevGrantRolesEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public DevGrantRolesEndpointTests(WebAppFactory factory) => _factory = factory;

    /// <summary>
    /// Create tenant-authenticated client via JWT for dev grant-roles endpoint tests.
    /// </summary>
    private static async Task<HttpClient> ClientAsync(WebAppFactory f, string tenantSlug)
    {
        // Phase A migration: use real password + login + select-tenant flow instead of legacy mint helper
        var c = f.CreateClient();
        await AuthTestClientFlow.LoginAndSelectTenantAsync(f, c, "kevin@example.com", tenantSlug);
        return c;
    }

    [Fact]
    public async Task Grants_roles_to_new_user_and_membership()
    {
    var client = await ClientAsync(_factory, "kevin-personal");

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = db.Tenants.First(t => t.Name == "kevin-personal").Id;
        }

        var email = $"dev-grant-{Guid.NewGuid():N}@ex.com";
        var resp = await client.PostAsJsonAsync("/api/dev/grant-roles", new { tenantId, email, roles = new[] { "Creator", "Learner" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await resp.Content.ReadFromJsonAsync<GrantResp>();
        payload.Should().NotBeNull();
        payload!.rolesValue.Should().Be((int)(Roles.Creator | Roles.Learner));

        // Idempotent second call with same roles
        var resp2 = await client.PostAsJsonAsync("/api/dev/grant-roles", new { tenantId, email, roles = new[] { "Creator", "Learner" } });
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload2 = await resp2.Content.ReadFromJsonAsync<GrantResp>();
        payload2!.rolesValue.Should().Be(payload.rolesValue);
    }

    [Fact]
    public async Task Updates_existing_membership_roles()
    {
    var client = await ClientAsync(_factory, "kevin-personal");
        Guid tenantId;
        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = db.Tenants.First(t => t.Name == "kevin-personal").Id;
            var user = new User { Id = Guid.NewGuid(), Email = $"existing-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Roles = Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            userId = user.Id;
        }

        string email;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            email = db.Users.First(u => u.Id == userId).Email;
        }

        var resp = await client.PostAsJsonAsync("/api/dev/grant-roles", new { tenantId, email, roles = new[] { "TenantAdmin", "Approver", "Creator", "Learner" } });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<GrantResp>();
        body!.rolesValue.Should().Be((int)(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner));
    }

    // Verifies that updating roles for an existing membership creates an Audit row capturing old/new flags.
    [Fact]
    public async Task Updates_existing_membership_roles_writes_audit()
    {
    var client = await ClientAsync(_factory, "kevin-personal");
        Guid tenantId;
        Guid userId;
        string email;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = db.Tenants.First(t => t.Name == "kevin-personal").Id;
            var user = new User { Id = Guid.NewGuid(), Email = $"audit-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Roles = Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            userId = user.Id;
            email = user.Email;
        }

        var resp = await client.PostAsJsonAsync("/api/dev/grant-roles", new { tenantId, email, roles = new[] { "Creator" } });
        resp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audits = db.Audits.Where(a => a.TenantId == tenantId && a.UserId == userId).ToList();
            audits.Should().NotBeEmpty();
            audits.Last().NewRoles.Should().Be(Roles.Creator);
            audits.Last().OldRoles.Should().Be(Roles.Learner);
        }
    }

    [Fact]
    public async Task Returns_400_for_invalid_role_name()
    {
    var client = await ClientAsync(_factory, "kevin-personal");
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = db.Tenants.First(t => t.Name == "kevin-personal").Id;
        }
        var resp = await client.PostAsJsonAsync("/api/dev/grant-roles", new { tenantId, email = "oops@example.com", roles = new[] { "NotARole" } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private record GrantResp(Guid userId, Guid tenantId, string roles, int rolesValue);
}