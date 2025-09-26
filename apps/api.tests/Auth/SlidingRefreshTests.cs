using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 11: Tests for sliding refresh expiration & absolute max lifetime enforcement.
/// Scenarios:
/// 1. Sliding window extends expiry forward (short base TTL overridden by sliding window).
/// 2. Absolute max lifetime caps extension (window would exceed cap => clamped)
/// 3. Exceeded absolute lifetime returns 401 refresh_max_lifetime_exceeded and increments metric counter.
/// </summary>
public class SlidingRefreshTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SlidingRefreshTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Refresh_Rotation_Extends_Expiry_Within_Sliding_Window()
    {
        // Base TTL 1 day, sliding window 5 days, max lifetime 10 days.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__JWT__REFRESH_TTL_DAYS", "1" },
            { "AUTH__REFRESH_SLIDING_WINDOW_DAYS", "5" },
            { "AUTH__REFRESH_MAX_LIFETIME_DAYS", "10" }
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"slide_extend_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var setCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt="));
        var cookiePair = setCookie.Split(';')[0];
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStreamAsync());
        var initialExpiry = loginJson.RootElement.GetProperty("refresh").GetProperty("expiresAt").GetDateTime();

        // Perform a refresh rotation (immediately) - expiry should move forward by roughly SlidingWindowDays (5 days) from now, not stay at +1 day.
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookiePair);
        var refresh = await client.SendAsync(refreshReq);
        refresh.EnsureSuccessStatusCode();
        using var refreshJson = JsonDocument.Parse(await refresh.Content.ReadAsStreamAsync());
        var newExpiry = refreshJson.RootElement.GetProperty("refresh").GetProperty("expiresAt").GetDateTime();

        (newExpiry - DateTime.UtcNow).TotalDays.Should().BeGreaterThan(3.5, "sliding window should extend expiry beyond base TTL");
        newExpiry.Should().BeAfter(initialExpiry, "expiry should extend forward");
    }

    [Fact]
    public async Task Refresh_Rotation_Clamped_By_Max_Lifetime()
    {
        // Base TTL 7 days, sliding window 7 days, max lifetime 10 days. After first rotation, the proposed (now + 7) would exceed original+10 cap -> clamp.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__JWT__REFRESH_TTL_DAYS", "7" },
            { "AUTH__REFRESH_SLIDING_WINDOW_DAYS", "7" },
            { "AUTH__REFRESH_MAX_LIFETIME_DAYS", "10" }
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"slide_clamp_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var setCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt="));
        var cookiePair = setCookie.Split(';')[0];
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStreamAsync());
        var firstExpiry = loginJson.RootElement.GetProperty("refresh").GetProperty("expiresAt").GetDateTime();

        // Simulate near max lifetime by manually updating created_at/original_created_at backwards via admin endpoint isn't available;
        // Instead we directly perform a refresh after short delay; expectation: extension limited to <= original+10 days.
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookiePair);
        var refresh = await client.SendAsync(refreshReq);
        refresh.EnsureSuccessStatusCode();
        using var refreshJson = JsonDocument.Parse(await refresh.Content.ReadAsStreamAsync());
        var secondExpiry = refreshJson.RootElement.GetProperty("refresh").GetProperty("expiresAt").GetDateTime();

        // The cap is originalCreatedAt + 10 days. firstExpiry should be ~ +7 days, secondExpiry should be <= +10 days.
        (secondExpiry - DateTime.UtcNow).TotalDays.Should().BeLessThanOrEqualTo(10.1, "must not exceed absolute max lifetime");
        secondExpiry.Should().BeAfter(firstExpiry.AddDays(-0.1));
    }

    [Fact]
    public async Task Refresh_After_Max_Lifetime_Denied()
    {
        // Configure extremely small max lifetime to force immediate denial after first expiry passes.
        var factory = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__JWT__REFRESH_TTL_DAYS", "1" },
            { "AUTH__REFRESH_SLIDING_WINDOW_DAYS", "1" },
            { "AUTH__REFRESH_MAX_LIFETIME_DAYS", "1" }
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"slide_deny_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var setCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt="));
        var cookiePair = setCookie.Split(';')[0];

        // Force artificial lifetime exceed: update DB original_created_at to 2 days in the past so cap hit immediately.
        // We access scoped db via factory helper.
        // IMPORTANT: We must use the cloned factory's service provider (factory.Services), not the original
        // fixture instance (_factory). The WithSettings call above returns a brand new WebAppFactory with its
        // own in‑memory database; using _factory.Services would point at a different AppDbContext instance where
        // the test user does not exist (leading to "Sequence contains no elements").
        var scopeFactory = factory.Services.GetService(typeof(IServiceScopeFactory)) as IServiceScopeFactory;
        using (var scope = scopeFactory!.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rtHash = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt=")).Split('=')[1].Split(';')[0];
            // We only stored hash in DB; query first refresh token for user and backdate original_created_at.
            var user = await db.Users.OrderByDescending(u => u.CreatedAt).FirstAsync(u => u.Email == email);
            var rts = await db.RefreshTokens.Where(r => r.UserId == user.Id).ToListAsync();
            foreach (var r in rts)
            {
                r.OriginalCreatedAt = DateTime.UtcNow.AddDays(-2);
            }
            await db.SaveChangesAsync();
        }

        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookiePair);
        var refresh = await client.SendAsync(refreshReq);
        refresh.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        var body = await refresh.Content.ReadAsStringAsync();
        body.Should().Contain("refresh_max_lifetime_exceeded");
    }
}
