using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 12: CSRF protection tests.
/// Validates double-submit cookie behavior across login & refresh endpoints.
/// Scenarios:
/// 1. Disabled mode: request without header succeeds.
/// 2. Enabled: missing cookie => 400 csrf_missing_cookie.
/// 3. Enabled: missing header => 400 csrf_missing_header.
/// 4. Enabled: mismatch header vs cookie => 400 csrf_mismatch.
/// 5. Enabled: correct header+cookie -> success (login + refresh flow).
/// 6. GET /api/auth/csrf issues token & cookie.
/// 7. Enabled: logout without header fails.
/// 8. Enabled: logout with header succeeds.
/// </summary>
public class CsrfTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public CsrfTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Csrf_Disabled_Allows_Login_Without_Header()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "false" }
        }).CreateClient();
        var email = $"csrf_disabled_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Csrf_Enabled_Missing_Cookie_Fails()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "false" }
        }).CreateClient();
        var email = $"csrf_missing_cookie_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("csrf_missing_cookie");
    }

    [Fact]
    public async Task Csrf_Enabled_Missing_Header_Fails()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" }
        }).CreateClient();
        var email = $"csrf_missing_header_{Guid.NewGuid()}@test.com";
        // Prime cookie via explicit GET
        var csrfResp = await client.GetAsync("/api/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        // Attempt login without header though cookie present
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Password123!" })
        };
        var login = await client.SendAsync(loginReq);
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("csrf_missing_header");
    }

    [Fact]
    public async Task Csrf_Enabled_Mismatch_Header_Fails()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" }
        }).CreateClient();
        var email = $"csrf_mismatch_{Guid.NewGuid()}@test.com";
        var csrf = await client.GetAsync("/api/auth/csrf");
        csrf.EnsureSuccessStatusCode();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Password123!" })
        };
        // Intentionally incorrect header value
        loginReq.Headers.Add("X-CSRF", "bogus123");
        var login = await client.SendAsync(loginReq);
        login.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await login.Content.ReadAsStringAsync()).Should().Contain("csrf_mismatch");
    }

    [Fact]
    public async Task Csrf_Enabled_Header_And_Cookie_Succeeds_Login_And_Refresh()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" },
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        var email = $"csrf_success_{Guid.NewGuid()}@test.com";
        var csrfResp = await client.GetAsync("/api/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();
        var csrfCookie = csrfResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("csrf="));
        var token = csrfCookie.Split('=')[1].Split(';')[0];
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Password123!" })
        };
        loginReq.Headers.Add("X-CSRF", token);
        var login = await client.SendAsync(loginReq);
        login.EnsureSuccessStatusCode();
        // Extract refresh cookie for refresh call
        var rtCookie = login.Headers.GetValues("Set-Cookie").FirstOrDefault(h => h.StartsWith("rt="));
        rtCookie.Should().NotBeNull();
        var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        refreshReq.Headers.Add("X-CSRF", token);
        if (rtCookie is not null) refreshReq.Headers.Add("Cookie", rtCookie.Split(';')[0]);
        var refresh = await client.SendAsync(refreshReq);
        refresh.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Csrf_Enabled_Logout_Without_Header_Fails()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" },
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        var email = $"csrf_logout_fail_{Guid.NewGuid()}@test.com";
        var csrfResp = await client.GetAsync("/api/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();
        var token = csrfResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("csrf=")).Split('=')[1].Split(';')[0];
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Password123!" })
        };
        loginReq.Headers.Add("X-CSRF", token);
        var login = await client.SendAsync(loginReq);
        login.EnsureSuccessStatusCode();
        var logout = await client.PostAsync("/api/auth/logout", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        logout.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await logout.Content.ReadAsStringAsync()).Should().Contain("csrf_missing_header");
    }

    [Fact]
    public async Task Csrf_Enabled_Logout_With_Header_Succeeds()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" },
            { "AUTH__REFRESH_COOKIE_ENABLED", "true" },
            { "AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT", "false" }
        }).CreateClient();
        var email = $"csrf_logout_ok_{Guid.NewGuid()}@test.com";
        var csrfResp = await client.GetAsync("/api/auth/csrf");
        csrfResp.EnsureSuccessStatusCode();
        var token = csrfResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("csrf=")).Split('=')[1].Split(';')[0];
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = "Password123!" })
        };
        loginReq.Headers.Add("X-CSRF", token);
        var login = await client.SendAsync(loginReq);
        login.EnsureSuccessStatusCode();
        // Extract refresh cookie for logout request (auth token in header already present via handler)
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        };
        logoutReq.Headers.Add("X-CSRF", token);
        var logout = await client.SendAsync(logoutReq);
        logout.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Csrf_Get_Issues_Token()
    {
        var client = _factory.WithSettings(new()
        {
            { "AUTH__CSRF__ENABLED", "true" },
            { "AUTH__CSRF__AUTO_ISSUE", "true" }
        }).CreateClient();
        var resp = await client.GetAsync("/api/auth/csrf");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("token");
        var setCookie = resp.Headers.GetValues("Set-Cookie").FirstOrDefault(c => c.StartsWith("csrf="));
        setCookie.Should().NotBeNull();
    }
}
