using System.Net;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class RefreshCookieTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshCookieTests(WebAppFactory factory) => _factory = factory;

    private HttpClient CreateManualCookieClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Extracts the refresh cookie header and plaintext token value from an auth response.
    private static (string CookieHeader, string TokenValue) ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var raw = cookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase));
        var header = raw.Split(';')[0];
        var token = header[(header.IndexOf('=') + 1)..];
        return (header, token);
    }

    [Fact]
    public async Task Login_Sets_Refresh_Cookie_When_Flag_Enabled()
    {
    var client = CreateManualCookieClient();
        // First sign up a user
        var email = $"cookieuser-{Guid.NewGuid():N}@example.com";
    var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "P@ssw0rd!1" });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);

    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "P@ssw0rd!1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
    setCookies!.Should().Contain(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase) && c.IndexOf("httponly", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public async Task SelectTenant_Rotates_Refresh_Cookie()
    {
    var client = CreateManualCookieClient();
        var email = $"rotuser-{Guid.NewGuid():N}@example.com";
    var signup2 = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "P@ssw0rd!1" });
    var signup = signup2; // preserve variable name usage below
        signup.EnsureSuccessStatusCode();

    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "P@ssw0rd!1" });
        login.EnsureSuccessStatusCode();
        var (firstRt, refreshToken) = ExtractRefreshCookie(login);

        var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        // Extract tenant slug from memberships (personal tenant slug is returned via signup->login membership projection)
        var memberships = body.GetProperty("memberships").EnumerateArray().ToList();
        memberships.Should().HaveCount(1);
        var tenantSlug = memberships[0].GetProperty("tenantSlug").GetString();
        tenantSlug.Should().NotBeNull();
    var selectReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/select-tenant")
        {
            Content = JsonContent.Create(new { tenant = tenantSlug, refreshToken })
        };
        selectReq.Headers.Add("Cookie", firstRt);
        var selectResp = await client.SendAsync(selectReq);
        selectResp.EnsureSuccessStatusCode();
        selectResp.Headers.TryGetValues("Set-Cookie", out var rotatedCookies).Should().BeTrue();
    var secondRt = rotatedCookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase)).Split(';')[0];
        secondRt.Should().NotBe(firstRt, "rotation should issue new cookie value");

        var selectBody = JsonDocument.Parse(await selectResp.Content.ReadAsStringAsync()).RootElement;
        selectBody.GetProperty("refresh").TryGetProperty("token", out _).Should().BeFalse();
    }
}
