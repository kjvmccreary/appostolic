using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Test helper utilities for auth flows reused across multiple test classes (e.g., TokenVersionCache, SlidingRefresh, CSRF).
/// Provides stable methods to sign up and login retrieving user id & access token for authenticated calls.
/// </summary>
public static class TestAuthClientFlow
{
    public static async Task<(Guid userId, string accessToken)> CreateUserAndLoginAsync(HttpClient client, WebAppFactory factory)
    {
        var email = $"helper_{Guid.NewGuid()}@test.com";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "Password123!" });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123!" });
        login.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        // access token in JSON path access.token (neutral)
        var access = json.RootElement.GetProperty("access").GetProperty("token").GetString();
        access.Should().NotBeNull();
        // decode to extract sub claim for user id
        var claims = WebAppFactory.DecodeJwtClaims(access!);
        var sub = claims.ContainsKey("sub") ? claims["sub"] : claims.GetValueOrDefault("nameid");
        Guid.TryParse(sub, out var userId).Should().BeTrue();
        return (userId, access!);
    }
}
