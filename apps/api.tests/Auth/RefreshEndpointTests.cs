using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Appostolic.Api.Tests.Auth;

public class RefreshEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshEndpointTests(WebAppFactory factory) => _factory = factory;

    private HttpClient CreateManualCookieClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // Helper: ensure a brand new user exists (signup) then perform login to obtain tokens/cookies.
    private static async Task<HttpResponseMessage> SignupAndLoginAsync(HttpClient client, string email, string password)
    {
        // Signup creates the user + personal tenant but does not issue tokens.
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        signup.EnsureSuccessStatusCode();
        // Now login to get access + refresh tokens (cookie + body during grace).
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        return login;
    }

    private static (string CookieHeader, string TokenValue) ExtractRtCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var rt = cookies!.First(c => c.StartsWith("rt="));
        var header = rt.Split(';')[0];
        var token = header[(header.IndexOf('=') + 1)..];
        return (header, token);
    }

    private static JsonDocument ReadJson(HttpResponseMessage response)
    {
        using var stream = response.Content.ReadAsStream();
        return JsonDocument.Parse(stream);
    }

    [Fact]
    public async Task Refresh_Succeeds_With_Cookie_Rotation()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"refreshcookie_{Guid.NewGuid()}@test.com";
        var login = await SignupAndLoginAsync(client, email, "Password123!");
    var (rtCookie, _) = ExtractRtCookie(login);
        // Call refresh using cookie (manually attach)
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Cookie", rtCookie);
        var refresh = await client.SendAsync(req);
        refresh.IsSuccessStatusCode.Should().BeTrue();
    var (rotatedRt, _) = ExtractRtCookie(refresh); // Ensure a new cookie is issued
    rotatedRt.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_Fails_On_Reusing_Rotated_Token()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"refreshreuse_{Guid.NewGuid()}@test.com";
        var login = await SignupAndLoginAsync(client, email, "Password123!");
    var (originalRt, _) = ExtractRtCookie(login);
        var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        firstReq.Headers.Add("Cookie", originalRt);
    var refresh1 = await client.SendAsync(firstReq);
        refresh1.IsSuccessStatusCode.Should().BeTrue();
        // Reuse original cookie against a fresh client (simulate theft)
    var rogue = CreateManualCookieClient();
        rogue.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
    var reuseReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    reuseReq.Headers.Add("Cookie", originalRt);
        var reuse = await rogue.SendAsync(reuseReq);
        reuse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Fails_When_Missing_Token()
    {
    var client = CreateManualCookieClient();
        var resp = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_Fails_When_Token_Expired()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
    var email = $"refreshexpired_{Guid.NewGuid()}@test.com";
    var login = await SignupAndLoginAsync(client, email, "Password123!");
        // Manually expire token in DB
    var scopeFactory = _factory.Services.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) as Microsoft.Extensions.DependencyInjection.IServiceScopeFactory;
    using var scope = scopeFactory!.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var token = await db.RefreshTokens.Where(r => r.Purpose == "neutral").OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
    token.Should().NotBeNull();
    token!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var expiredReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    expiredReq.Headers.Add("Cookie", ExtractRtCookie(login).CookieHeader);
        var resp = await client.SendAsync(expiredReq);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Fails_When_Token_Revoked_Reused()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"refreshrevoked_{Guid.NewGuid()}@test.com";
        var login = await SignupAndLoginAsync(client, email, "Password123!");
    var (originalRt, _) = ExtractRtCookie(login);
        // First rotation
        var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        firstReq.Headers.Add("Cookie", originalRt);
        var first = await client.SendAsync(firstReq);
        first.IsSuccessStatusCode.Should().BeTrue();
        // Reuse old cookie on rogue client
    var rogueClient = CreateManualCookieClient();
        rogueClient.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var reuseReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        reuseReq.Headers.Add("Cookie", originalRt);
        var reuse = await rogueClient.SendAsync(reuseReq);
        reuse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Can_Issue_TenantToken()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"refreshtenant_{Guid.NewGuid()}@test.com";
        var login = await SignupAndLoginAsync(client, email, "Password123!");
        using var loginDoc = ReadJson(login);
        var slug = loginDoc.RootElement.GetProperty("memberships")[0].GetProperty("tenantSlug").GetString();
        slug.Should().NotBeNull();
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/refresh?tenant={slug}") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    req.Headers.Add("Cookie", ExtractRtCookie(login).CookieHeader);
        var refresh = await client.SendAsync(req);
        refresh.IsSuccessStatusCode.Should().BeTrue();
        using var refreshDoc = ReadJson(refresh);
        refreshDoc.RootElement.GetProperty("tenantToken").GetProperty("access").GetProperty("type").GetString().Should().Be("tenant");
    }

    // Verifies clients must supply refresh tokens via cookie; body-only submissions are rejected.
    [Fact]
    public async Task Refresh_Fails_When_Only_Body_Token_Is_Provided()
    {
    var client = CreateManualCookieClient();
        client.DefaultRequestHeaders.Add("X-Test-HTTPS", "1");
        var email = $"refreshbody_{Guid.NewGuid()}@test.com";
        var login = await SignupAndLoginAsync(client, email, "Password123!");
        var (_, refreshToken) = ExtractRtCookie(login);
        refreshToken.Should().NotBeNull();
        var body = JsonSerializer.Serialize(new { refreshToken });
        // Intentionally omit cookie to ensure cookie-only steady state rejects body tokens
        var refresh = await client.PostAsync("/api/auth/refresh", new StringContent(body, Encoding.UTF8, "application/json"));
        refresh.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        using var doc = ReadJson(refresh);
        doc.RootElement.GetProperty("code").GetString().Should().Be("missing_refresh");
    }
}
