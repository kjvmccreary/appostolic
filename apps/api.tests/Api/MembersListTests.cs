using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using System.Net.Http.Headers;

namespace Appostolic.Api.Tests.Api;

public class MembersListTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public MembersListTests(WebAppFactory factory) => _factory = factory;

    // Story 5 Refactor: Use TestAuthSeeder to issue tenant-scoped tokens directly.
    // These tests focus on authorization behavior of listing members, not the auth pipeline.

    private static async Task<(HttpClient client, Guid tenantId)> CreateOwnerAsync(WebAppFactory factory, string email, string tenant)
    {
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(factory, email, tenant, owner: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, tenantId);
    }

    private static async Task<HttpClient> CreateViewerAsync(WebAppFactory factory, string email, Guid tenantId, string tenantSlug)
    {
        // Ensure membership with no admin flags
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null)
            {
                user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenantId);
            if (membership == null)
            {
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        var (viewerToken, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(factory, email, tenantSlug, owner: false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        return client;
    }

    [Fact]
    public async Task Owner_can_list_members()
    {
        var (owner, tenantId) = await CreateOwnerAsync(_factory, "kevin@example.com", "kevin-personal");

        var resp = await owner.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Viewer_gets_403_for_members_list()
    {
        var (ownerClient, tenantId) = await CreateOwnerAsync(_factory, "kevin@example.com", "kevin-personal"); // owner not used further, just ensures tenant exists
        var viewerEmail = $"viewer-{Guid.NewGuid():N}@example.com";
        var viewer = await CreateViewerAsync(_factory, viewerEmail, tenantId, "kevin-personal");
        var resp = await viewer.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_or_403()
    {
        using var unauth = _factory.CreateClient(); // No auth performed
        var (_, tenantId) = await CreateOwnerAsync(_factory, "kevin@example.com", "kevin-personal");

        var resp = await unauth.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
