using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using Appostolic.Api.Tests.TestUtilities;

namespace Appostolic.Api.Tests.Api;

/// <summary>
/// Story 4 (refLeg-04/Phase 2) regression tests around legacy write-path inputs using the removed
/// single 'role' field.
///
/// Current behavior (post legacy convergence removal):
///  - Invite creation: the DTO no longer defines a legacy 'role' field. Supplying only 'role' therefore
///    results in zero parsed flags and the generic NO_FLAGS validation error (the earlier
///    LEGACY_ROLE_DEPRECATED code was only emitted while the transitional legacy field still existed).
///  - Member role change (legacy single-role endpoint): endpoint itself is now hard‑deprecated and always
///    returns 400 LEGACY_ROLE_DEPRECATED to force client migration to flags endpoint.
///
/// These tests lock the above invariants so future refactors don't accidentally re‑introduce legacy
/// mutation paths.
/// </summary>
public class LegacyRoleWritePathDeprecationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRoleWritePathDeprecationTests(WebAppFactory factory) => _factory = factory;
    // Story: JWT auth refactor – migrate to deterministic TestAuthSeeder issuance. Each test now
    // creates a unique tenant + acting owner user (full role flags) without relying on the pre-seeded
    // kevin@example.com / kevin-personal pair or password login/select flow.

    // Local Unique* helpers removed; using shared UniqueId

    private async Task<(HttpClient client, Guid tenantId)> CreateOwnerClientAsync(string scenario)
    {
    var email = UniqueId.Email("legacyrole");
    var slug = UniqueId.Slug(scenario);
        var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slug, owner: true);
        // Optionally seed password hash for consistency (not required for bearer auth)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            var (hash, salt, _) = hasher.HashPassword(TestAuthSeeder.DefaultPassword);
            db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, tenantId);
    }

    [Fact]
    public async Task Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS()
    {
        var (client, tenantId) = await CreateOwnerClientAsync("invite-legacy-role");

        var resp = await client.PostAsJsonAsync($"/api/tenants/{tenantId}/invites", new { email = $"legacy-invite-{Guid.NewGuid():N}@ex.com", role = "Editor" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.TryGetProperty("code", out var codeEl).Should().BeTrue();
    // Legacy 'role' value is ignored; absence of flags yields NO_FLAGS validation code.
    codeEl.GetString().Should().Be("NO_FLAGS");
    }

    [Fact]
    public async Task Member_role_change_with_legacy_role_is_rejected()
    {
        var (client, tenantId) = await CreateOwnerClientAsync("member-legacy-role");
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
