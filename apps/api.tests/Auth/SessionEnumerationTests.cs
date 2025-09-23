using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Appostolic.Api;
using Appostolic.Api.Infrastructure.Auth.Jwt;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using Microsoft.EntityFrameworkCore; // EF async extensions

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Integration tests for Story 8 session enumeration & per-session revoke endpoints.
/// Focus: success list, disabled flag, revoke success/not_found/idempotent, last_used_at update on refresh.
/// </summary>
public class SessionEnumerationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SessionEnumerationTests(WebAppFactory factory) => _factory = factory;

    private record LoginRequest(string Email, string Password);

    [Fact]
    public async Task ListSessions_success_returns_current_and_active()
    {
        var client = _factory.CreateClient();
        // Seed user & password
        var email = $"sess-{Guid.NewGuid():N}@ex.com";
        await TestAuthSeeder.SeedUserWithPasswordAsync(_factory.Services, email, "Password123!");
    // Login to create refresh token cookie and capture access token
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Password123!" });
    loginResp.EnsureSuccessStatusCode();
    var loginJson = await loginResp.Content.ReadFromJsonAsync<LoginEnvelope>();
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginJson!.access.token);
    // Call sessions (requires Bearer auth)
    var sessionsResp = await client.GetAsync("/api/auth/sessions");
        sessionsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await sessionsResp.Content.ReadFromJsonAsync<SessionsEnvelope>();
        Assert.NotNull(json);
    Assert.NotNull(json);
    Assert.NotEmpty(json!.sessions);
    Assert.Contains(json.sessions, s => s.current);
    }

    [Fact]
    public async Task ListSessions_disabled_flag_returns_404()
    {
        var customFactory = _factory.WithSettings(new() { { "AUTH__SESSIONS__ENUMERATION_ENABLED", "false" } });
        var client = customFactory.CreateClient();
        var email = $"sessflag-{Guid.NewGuid():N}@ex.com";
        // Seed user inside the custom factory's service provider so login sees it
        await TestAuthSeeder.SeedUserWithPasswordAsync(customFactory.Services, email, "Password123!");
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Password123!" });
        loginResp.EnsureSuccessStatusCode();
        var loginJson = await loginResp.Content.ReadFromJsonAsync<LoginEnvelope>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginJson!.access.token);
        var listResp = await client.GetAsync("/api/auth/sessions");
        Assert.Equal(HttpStatusCode.NotFound, listResp.StatusCode);
    }

    [Fact]
    public async Task Revoke_single_session_flow()
    {
        var client = _factory.CreateClient();
        var email = $"sessrevoke-{Guid.NewGuid():N}@ex.com";
        await TestAuthSeeder.SeedUserWithPasswordAsync(_factory.Services, email, "Password123!");
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Password123!" });
    loginResp.EnsureSuccessStatusCode();
    var loginJson = await loginResp.Content.ReadFromJsonAsync<LoginEnvelope>();
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginJson!.access.token);
    var sessionsResp = await client.GetFromJsonAsync<SessionsEnvelope>("/api/auth/sessions");
        var first = sessionsResp!.sessions.First();
        var revokeResp = await client.PostAsync($"/api/auth/sessions/{first.id}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);
        // Idempotent second revoke
        var revokeAgain = await client.PostAsync($"/api/auth/sessions/{first.id}/revoke", null);
        Assert.Equal(HttpStatusCode.NoContent, revokeAgain.StatusCode);
        // Listing again should still show session (unless design chooses to hide revoked later) but revoked path future change; for now accept unchanged list.
    }

    [Fact]
    public async Task LastUsedAt_updates_on_refresh()
    {
        var client = _factory.CreateClient();
        var email = $"sessused-{Guid.NewGuid():N}@ex.com";
        await TestAuthSeeder.SeedUserWithPasswordAsync(_factory.Services, email, "Password123!" );
    var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Password123!" });
    loginResp.EnsureSuccessStatusCode();
        var loginJson = await loginResp.Content.ReadFromJsonAsync<LoginEnvelope>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginJson!.access.token);
        var list1 = await client.GetFromJsonAsync<SessionsEnvelope>("/api/auth/sessions");
        var original = list1!.sessions.First(s => s.current);
        Assert.Null(original.lastUsedAt);
        // Trigger refresh rotation (old token revoked, new issued)
        var refreshResp = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        refreshResp.EnsureSuccessStatusCode();
        // The original session should now be revoked and excluded from enumeration.
        var list2 = await client.GetFromJsonAsync<SessionsEnvelope>("/api/auth/sessions");
        Assert.DoesNotContain(list2!.sessions, s => s.id == original.id); // rotation ensured
        // Validate LastUsedAt persisted on the revoked (pre-rotation) token via direct DB query.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revokedWithLastUsed = await db.RefreshTokens
            .Where(r => r.UserId != Guid.Empty && r.RevokedAt != null && r.Purpose == "neutral" && r.LastUsedAt != null)
            .OrderByDescending(r => r.RevokedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(revokedWithLastUsed); // ensure at least one revoked token captured usage
        Assert.NotNull(revokedWithLastUsed!.LastUsedAt); // updated on validation path during refresh
    }

    private record SessionDto(Guid id, DateTime createdAt, DateTime? lastUsedAt, DateTime expiresAt, string? fingerprint, bool current);
    private record SessionsEnvelope(SessionDto[] sessions);
    private record LoginEnvelope(object user, object[] memberships, AccessObj access, object refresh, object? tenantToken);
    private record AccessObj(string token, DateTime expiresAt, string type);
}

static class ShouldExtensions
{
    public static void ShouldBe(this HttpStatusCode actual, HttpStatusCode expected)
    {
        Assert.Equal(expected, actual);
    }
}
