using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.Tests;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.AuthTests;

public class LoginJwtNeutralTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LoginJwtNeutralTests(WebAppFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static (string CookieHeader, string TokenValue) ExtractRefreshCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var raw = cookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase));
        var header = raw.Split(';')[0];
        var token = header[(header.IndexOf('=') + 1)..];
        return (header, token);
    }

    [Fact]
    public async Task Login_ReturnsNeutralAccessAndRefreshToken()
    {
        using var client = _factory.CreateClient();

        // Sign up a user
        var email = $"neutral-{Guid.NewGuid():N}@example.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();

        // Login (new JWT response shape)
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        var doc = await login.Content.ReadFromJsonAsync<JsonObject>();
        doc.Should().NotBeNull();
        doc!["access"]!.Should().NotBeNull();
    doc!["refresh"]!.Should().NotBeNull();
    doc!["access"]!["token"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    var (refreshCookie, refreshToken) = ExtractRefreshCookie(login);
    refreshCookie.Should().StartWith("rt=");
    refreshToken.Should().NotBeNullOrWhiteSpace();
    var refreshObj = doc!["refresh"]!.AsObject();
    refreshObj.ContainsKey("token").Should().BeFalse();
    refreshObj["expiresAt"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    // Current implementation: when user has a single membership a tenant access token is auto-issued.
    doc.ContainsKey("tenantToken").Should().BeTrue();
    var tenantToken = doc["tenantToken"]!.AsObject();
    tenantToken["access"]!.Should().NotBeNull();
    tenantToken["access"]!["token"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

        // Ensure refresh token persisted hashed (not plaintext) in DB
        // Access test db via factory service provider
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var rt = await db.Set<Appostolic.Api.Infrastructure.Auth.Jwt.RefreshToken>().Where(r => r.UserId == userId).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync();
        rt.Should().NotBeNull();
        // TokenHash should be base64 of SHA256 -> length ~44 (depends on Convert.ToBase64String) > 40
        rt!.TokenHash.Length.Should().BeGreaterThan(40);
    }
}
