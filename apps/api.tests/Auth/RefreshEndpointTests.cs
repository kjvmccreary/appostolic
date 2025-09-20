using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class RefreshEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshEndpointTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Refresh_Succeeds_With_Cookie_Rotation()
    {
        var client = _factory.CreateClient();
        var email = $"refreshcookie_{Guid.NewGuid()}@test.com";
        // Signup/login (assuming helper login path) - use login
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        login.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        setCookies!.Should().Contain(c => c.StartsWith("rt="));
        // Call refresh using cookie
        var refresh = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        refresh.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        refresh.Headers.TryGetValues("Set-Cookie", out var rotatedCookies).Should().BeTrue();
        rotatedCookies!.Count(c => c.StartsWith("rt=")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_Fails_On_Reusing_Rotated_Token()
    {
        var client = _factory.CreateClient();
        var email = $"refreshreuse_{Guid.NewGuid()}@test.com";
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var rtCookie = cookies!.First(c => c.StartsWith("rt="));
        var refresh1 = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        refresh1.IsSuccessStatusCode.Should().BeTrue();
        // Attempt reuse by manually sending old cookie (client auto replaced; craft new request)
        var handlerClient = _factory.CreateClient();
        handlerClient.DefaultRequestHeaders.Add("Cookie", rtCookie.Split(';')[0]);
        var reuse = await handlerClient.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        reuse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Fails_When_Missing_Token()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_Fails_When_Token_Expired()
    {
        var client = _factory.CreateClient();
        var email = $"refreshexpired_{Guid.NewGuid()}@test.com";
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        // Manually expire token in DB
    var scopeFactory = _factory.Services.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) as Microsoft.Extensions.DependencyInjection.IServiceScopeFactory;
    using var scope = scopeFactory!.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var token = await db.RefreshTokens.Where(r => r.Purpose == "neutral").OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
    token.Should().NotBeNull();
    token!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var resp = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Fails_When_Token_Revoked_Reused()
    {
        var client = _factory.CreateClient();
        var email = $"refreshrevoked_{Guid.NewGuid()}@test.com";
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var first = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        first.IsSuccessStatusCode.Should().BeTrue();
        // Use old cookie extracted from initial login (captured from handler manually)
        login.Headers.TryGetValues("Set-Cookie", out var originalCookies).Should().BeTrue();
        var originalRt = originalCookies!.First(c => c.StartsWith("rt="));
        var rogueClient = _factory.CreateClient();
        rogueClient.DefaultRequestHeaders.Add("Cookie", originalRt.Split(';')[0]);
        var reuse = await rogueClient.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        reuse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Can_Issue_TenantToken()
    {
        var client = _factory.CreateClient();
        var email = $"refreshtenant_{Guid.NewGuid()}@test.com";
        // Create multi-tenant user via two logins with separate tenant creation (placeholder: assume magic or helper; using login only creates personal tenant)
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var loginJson = await login.Content.ReadFromJsonAsync<dynamic>();
        var memberships = (IEnumerable<dynamic>)loginJson!.memberships;
        var slug = memberships.First().tenantSlug;
        var refresh = await client.PostAsync($"/api/auth/refresh?tenant={slug}", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        refresh.IsSuccessStatusCode.Should().BeTrue();
        var json = await refresh.Content.ReadFromJsonAsync<dynamic>();
        ((string)json!.tenantToken.access.type).Should().Be("tenant");
    }

    [Fact]
    public async Task Refresh_Allows_Body_Token_During_Grace()
    {
        var client = _factory.CreateClient();
        var email = $"refreshbody_{Guid.NewGuid()}@test.com";
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        // Extract refresh token from JSON body (grace mode still returns it)
        var loginJson = await login.Content.ReadFromJsonAsync<dynamic>();
        string refreshToken = loginJson!.refresh.token;
        var body = System.Text.Json.JsonSerializer.Serialize(new { refreshToken });
        var refresh = await client.PostAsync("/api/auth/refresh", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        refresh.IsSuccessStatusCode.Should().BeTrue();
    }
}
