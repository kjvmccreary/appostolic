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

    // RDH Story 2: helper now mints JWT tokens instead of dev headers.
    private static async Task<HttpClient> ClientAsync(WebAppFactory f, string? email = null, string? tenantSlug = null)
    {
        var c = f.CreateClient();
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(tenantSlug))
        {
            await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, email, tenantSlug);
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            await Appostolic.Api.AuthTests.AuthTestClient.UseNeutralAsync(c, email);
        }
        return c;
    }

    [Fact]
    public async Task Owner_can_list_members()
    {
    var owner = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");

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

    var viewer = await ClientAsync(_factory, viewerEmail, "kevin-personal");
        var resp = await viewer.GetAsync($"/api/tenants/{tenantId}/members");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_or_403()
    {
    using var unauth = await ClientAsync(_factory); // no auth headers minted
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
