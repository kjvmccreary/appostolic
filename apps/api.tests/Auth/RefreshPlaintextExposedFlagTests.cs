using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Cookie-only steady state: verify plaintext refresh tokens are never emitted in JSON responses.
/// </summary>
public class RefreshPlaintextSuppressionTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshPlaintextSuppressionTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Omits_Plaintext_By_Default()
    {
        var client = _factory.CreateClient();
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
    public async Task Refresh_Omits_Plaintext_With_Cookie()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"flagrefreshhide_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        // Extract cookie only; ignore body token (shouldn't exist) and call refresh
        var setCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("rt="));
        var cookieHeader = setCookie.Split(';')[0];
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", cookieHeader);
        var refresh = await client.SendAsync(refreshReq);
        refresh.EnsureSuccessStatusCode();
        using var refreshJson = JsonDocument.Parse(await refresh.Content.ReadAsStreamAsync());
        var refreshObj = refreshJson.RootElement.GetProperty("refresh");
        refreshObj.TryGetProperty("token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Login_Omits_Plaintext_Even_Without_Cookie()
    {
        // Ensure cookie issuance disabled to prove JSON omission is unconditional.
        var client = _factory.WithSettings(new()
        {
            { "AUTH__REFRESH_COOKIE_ENABLED", "false" }
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
