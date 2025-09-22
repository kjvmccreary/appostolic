using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    // RDH Story 2 Phase A: migrate to real password login + select-tenant flows.
    private const string DefaultPw = "Password123!"; // must align with AuthTestClientFlow.DefaultPassword

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
    public async Task Invite_with_legacy_role_only_is_rejected_with_NO_FLAGS()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");
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
    // Legacy 'role' value is ignored; absence of flags yields NO_FLAGS validation code.
    codeEl.GetString().Should().Be("NO_FLAGS");
    }

    [Fact]
    public async Task Member_role_change_with_legacy_role_is_rejected()
    {
        await SeedPasswordAsync("kevin@example.com", DefaultPw);
        var client = _factory.CreateClient();
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(_factory, client, "kevin@example.com", "kevin-personal");
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
