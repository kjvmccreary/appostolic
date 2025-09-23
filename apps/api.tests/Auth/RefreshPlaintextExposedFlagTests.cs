using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 2: Verifies plaintext refresh token is omitted when exposure flag disabled (suppression scenarios).
/// </summary>
public class RefreshPlaintextExposedFlagTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshPlaintextExposedFlagTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Omits_Plaintext_When_Disabled()
    {
        // Explicitly disable plaintext exposure flag for this suppression scenario. We also keep
        // the refresh cookie enabled to mirror production cookie-first behavior; the assertion
        // should hold independent of cookie issuance.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"flagloginhide_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStreamAsync());
        var refreshElement = loginJson.RootElement.GetProperty("refresh");
        refreshElement.TryGetProperty("token", out _).Should().BeFalse("plaintext token should be omitted when flag disabled");
        refreshElement.GetProperty("expiresAt").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Refresh_Omits_Plaintext_When_Disabled()
    {
        // Disable plaintext exposure flag while allowing grace path + cookie issuance.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_GRACE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"flagrefreshhide_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        // Extract cookie only; ignore body token (shouldn't exist) and call refresh
        var setCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt="));
        var cookieHeader = setCookie.Split(';')[0];
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        refreshReq.Headers.Add("Cookie", cookieHeader);
        var refresh = await client.SendAsync(refreshReq);
        refresh.EnsureSuccessStatusCode();
        using var refreshJson = JsonDocument.Parse(await refresh.Content.ReadAsStreamAsync());
        var refreshObj = refreshJson.RootElement.GetProperty("refresh");
        refreshObj.TryGetProperty("token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Login_Omits_Plaintext_When_Disabled_NoCookieOverride()
    {
        // Even with no explicit cookie override, disabling plaintext flag should suppress token.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"flaglogindefault_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStreamAsync());
        var refreshElement = loginJson.RootElement.GetProperty("refresh");
        refreshElement.TryGetProperty("token", out _).Should().BeFalse();
    }
}
