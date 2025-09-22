using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class MembersListTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public MembersListTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2 Phase A: migrate from mint helper (test-only token issuance) to real auth flow.
    // We seed a known password (matches AuthTestClientFlow default) then perform:
    //   POST /api/auth/login  -> obtain neutral access + refresh
    //   POST /api/auth/select-tenant -> obtain tenant-scoped access token
    // This ensures the members listing endpoint is exercised under production authentication paths.
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
    public async Task Owner_can_list_members()
    {
        // Seed owner password (factory seeds user & membership already)
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var owner = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, owner, "kevin@example.com", "kevin-personal");

        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
        }

        var resp = await owner.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Viewer_gets_403_for_members_list()
    {
        Guid tenantId;
        string viewerEmail;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            viewerEmail = $"viewer-{Guid.NewGuid():N}@example.com";
            var u = new User { Id = Guid.NewGuid(), Email = viewerEmail, CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = u.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        // Seed viewer password & perform real auth + tenant selection (roles = Learner only)
        await SeedPasswordAsync(viewerEmail, DefaultPw);
        var viewer = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, viewer, viewerEmail, "kevin-personal");
        var resp = await viewer.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_or_403()
    {
        using var unauth = _factory.CreateClient(); // No auth performed
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
        }

        var resp = await unauth.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
