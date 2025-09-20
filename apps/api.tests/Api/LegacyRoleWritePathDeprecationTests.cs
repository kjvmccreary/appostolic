using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

/// <summary>
/// Story 4 (refLeg-04/Phase 2) regression tests ensuring legacy write-path inputs using the deprecated
/// 'role' field are rejected for invites AND member role changes. Member role change endpoint now returns
/// 400 LEGACY_ROLE_DEPRECATED (flags-only model enforced).
/// </summary>
public class LegacyRoleWritePathDeprecationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRoleWritePathDeprecationTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient Client(WebAppFactory f, string email, string tenantSlug)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", email);
        c.DefaultRequestHeaders.Add("x-tenant", tenantSlug);
        return c;
    }

    [Fact]
    public async Task Invite_with_legacy_role_only_is_rejected()
    {
        var client = Client(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
        }

        var resp = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", new { email = $"legacy-invite-{Guid.NewGuid():N}@ex.com", role = "Editor" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("code", out var codeEl).Should().BeTrue();
        codeEl.GetString().Should().Be("LEGACY_ROLE_DEPRECATED");
    }

    [Fact]
    public async Task Member_role_change_with_legacy_role_is_rejected()
    {
        var client = Client(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId; Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            var u = new User { Id = Guid.NewGuid(), Email = $"member-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = u.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            targetUserId = u.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/tenants/{tenantId}/members/{targetUserId}", new { role = "Editor" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("code", out var codeEl).Should().BeTrue();
        codeEl.GetString().Should().Be("LEGACY_ROLE_DEPRECATED");
    }
}
