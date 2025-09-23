using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Integration tests validating refresh endpoint rate limiting behavior.
/// Uses small window & max thresholds to force deterministic blocking.
/// </summary>
public class RefreshRateLimitIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshRateLimitIntegrationTests(WebAppFactory factory) => _factory = factory;

    private static async Task<(HttpClient client, string rtCookie)> SignupAndLoginAsync(WebAppFactory factory, string email, bool https = true)
    {
        var client = factory.CreateClient();
        if (https) client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var password = "Password123!";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var rt = cookies!.First(c => c.StartsWith("rt="));
        return (client, rt.Split(';')[0]);
    }

    private static HttpRequestMessage RefreshRequest(string rtCookie)
        => new(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            Headers = { { "Cookie", rtCookie } }
        };

    private static string? TryExtractRtCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var rt = cookies.FirstOrDefault(c => c.StartsWith("rt="));
        return rt?.Split(';')[0];
    }

    [Fact]
    public async Task Blocks_After_Exceeding_Max_In_Window()
    {
        // Single evaluation semantics: each successful refresh increments attempts by 1 (after token lookup with userId).
        // We set Max=2 so third refresh exceeds (attempts: 1 OK, 2 OK, 3 > 2 => 429).
    var factory = _factory.WithSettings(new Dictionary<string, string?>
        {
            ["AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS"] = "60",
            ["AUTH__REFRESH_RATE_LIMIT_MAX"] = "2",
            ["AUTH__REFRESH_RATE_LIMIT_DRY_RUN"] = "false"
        });
        var email = $"rlblock_{Guid.NewGuid()}@test.com";
        var (client, rtCookie) = await SignupAndLoginAsync(factory, email);
        // First refresh
        var r1 = await client.SendAsync(RefreshRequest(rtCookie));
        r1.IsSuccessStatusCode.Should().BeTrue();
        rtCookie = TryExtractRtCookie(r1) ?? rtCookie;
        // Second refresh
        var r2 = await client.SendAsync(RefreshRequest(rtCookie));
        r2.IsSuccessStatusCode.Should().BeTrue();
        rtCookie = TryExtractRtCookie(r2) ?? rtCookie;
    // Third should block (429)
        var blocked = await client.SendAsync(RefreshRequest(rtCookie));
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var json = JsonDocument.Parse(await blocked.Content.ReadAsStreamAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be("refresh_rate_limited");
    }

    [Fact]
    public async Task DryRun_Does_Not_Block()
    {
    var factory = _factory.WithSettings(new Dictionary<string, string?>
        {
            ["AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS"] = "60",
            ["AUTH__REFRESH_RATE_LIMIT_MAX"] = "2",
            ["AUTH__REFRESH_RATE_LIMIT_DRY_RUN"] = "true"
        });
        var email = $"rldry_{Guid.NewGuid()}@test.com";
        var (client, rtCookie) = await SignupAndLoginAsync(factory, email);
        for (int i = 0; i < 5; i++)
        {
            var resp = await client.SendAsync(RefreshRequest(rtCookie));
            resp.IsSuccessStatusCode.Should().BeTrue();
            rtCookie = TryExtractRtCookie(resp) ?? rtCookie;
        }
    }

    [Fact]
    public async Task PerUser_Isolation_Shared_Ip()
    {
        var factory = _factory.WithSettings(new Dictionary<string, string?>
        {
            // Single evaluation semantics: Max=1 allows first refresh (attempt=1) and blocks second (attempt=2 > 1).
            ["AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS"] = "60",
            ["AUTH__REFRESH_RATE_LIMIT_MAX"] = "1",
            ["AUTH__REFRESH_RATE_LIMIT_DRY_RUN"] = "false"
        });
        // User A
        var emailA = $"rluserA_{Guid.NewGuid()}@test.com";
        var (clientA, rtCookieA) = await SignupAndLoginAsync(factory, emailA);
        // User B
        var emailB = $"rluserB_{Guid.NewGuid()}@test.com";
        var (clientB, rtCookieB) = await SignupAndLoginAsync(factory, emailB);
        // Each performs one refresh (allowed)
        var aFirst = await clientA.SendAsync(RefreshRequest(rtCookieA));
        aFirst.IsSuccessStatusCode.Should().BeTrue();
        rtCookieA = TryExtractRtCookie(aFirst) ?? rtCookieA;
        var bFirst = await clientB.SendAsync(RefreshRequest(rtCookieB));
        bFirst.IsSuccessStatusCode.Should().BeTrue();
        rtCookieB = TryExtractRtCookie(bFirst) ?? rtCookieB;
    // Second refresh (each should now exceed per-user window)
        var aSecond = await clientA.SendAsync(RefreshRequest(rtCookieA));
        aSecond.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var bSecond = await clientB.SendAsync(RefreshRequest(rtCookieB));
        bSecond.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
