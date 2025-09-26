using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class LogoutTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LogoutTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateManualCookieClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static (string CookieHeader, string TokenValue) ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var raw = cookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase));
        var header = raw.Split(';')[0];
        var token = header[(header.IndexOf('=') + 1)..];
        return (header, token);
    }

    [Fact]
    public async Task AccessToken_AllowsMeEndpoint_BeforeLogout()
    {
    var client = CreateManualCookieClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email = "diagme@example.com", password = "Pass1234!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "diagme@example.com", password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var access = json.RootElement.GetProperty("access").GetProperty("token").GetString();
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var meResp = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);
    }

    [Fact]
    public async Task SingleLogout_FollowedByRefresh_ReturnsReuse()
    {
        var client = CreateManualCookieClient();
        // 1. Signup/login to obtain refresh (body fallback path) & access
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email = "logout1@example.com", password = "Pass1234!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "logout1@example.com", password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var (refreshCookie, refresh) = ExtractRefreshCookie(login);
        var access = json.RootElement.GetProperty("access").GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refresh));
        Assert.False(string.IsNullOrWhiteSpace(access));

    // 2. Logout with provided refresh token via body while manually attaching refresh cookie
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = refresh })
        };
        logoutReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        logoutReq.Headers.Add("Cookie", refreshCookie);
        var logout = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // 3. Attempt refresh using same refresh token should yield reuse/invalid (reuse per design)
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", refreshCookie);
        var refreshResp = await client.SendAsync(refreshReq);
        var problem = JsonDocument.Parse(await refreshResp.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
        var code = problem.RootElement.GetProperty("code").GetString();
        Assert.True(code == "refresh_reuse" || code == "refresh_invalid", $"Unexpected code {code}");
    }

    [Fact]
    public async Task LogoutAll_RevokesAccessTokenVersion()
    {
    var client = CreateManualCookieClient();
        var email = "logoutall@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Pass1234!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
    var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
    var (refreshCookie, refresh) = ExtractRefreshCookie(login);
    var access = loginJson.RootElement.GetProperty("access").GetProperty("token").GetString();
        Assert.NotNull(access);
        Assert.NotNull(refresh);

        // Logout all
        var logoutAllReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout/all");
        logoutAllReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var logoutAllResp = await client.SendAsync(logoutAllReq);
        Assert.Equal(HttpStatusCode.NoContent, logoutAllResp.StatusCode);

        // Original access token should now fail on a protected endpoint (e.g., /api/me)
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var meResp = await client.SendAsync(meReq);
        // Could be 401 (token version mismatch) depending on auth handler implementation
        Assert.Equal(HttpStatusCode.Unauthorized, meResp.StatusCode);

        // Refresh attempt with old refresh token should also now be unauthorized (either invalid or reuse)
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshReq.Headers.Add("Cookie", refreshCookie);
        var refreshResp = await client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResp.StatusCode);
    }

    [Fact]
    public async Task MissingRefreshToken_OnSingleLogout_Returns400()
    {
    var client = CreateManualCookieClient();
        var email = "missingrtlogout@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Pass1234!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass1234!" });
        login.EnsureSuccessStatusCode();
        var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var access = loginJson.RootElement.GetProperty("access").GetProperty("token").GetString();

        // Do not supply token
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        logoutReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var logoutResp = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.BadRequest, logoutResp.StatusCode);
        var doc = JsonDocument.Parse(await logoutResp.Content.ReadAsStringAsync());
        Assert.Equal("missing_refresh", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_IdempotentSecondCall()
    {
        var client = _factory.CreateClient();
        var email = "idempotentlogout@example.com";
    await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Pass1234!" });
    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass1234!" });
    var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
    var (refreshCookie, refresh) = ExtractRefreshCookie(login);
    var access = json.RootElement.GetProperty("access").GetProperty("token").GetString();

    var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create(new { refreshToken = refresh }) };
        firstReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
    firstReq.Headers.Add("Cookie", refreshCookie);
        var first = await client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

    var secondReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout") { Content = JsonContent.Create(new { refreshToken = refresh }) };
        secondReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
    secondReq.Headers.Add("Cookie", refreshCookie);
        var second = await client.SendAsync(secondReq);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }
}
