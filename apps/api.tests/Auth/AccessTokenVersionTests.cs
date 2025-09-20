using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class AccessTokenVersionTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public AccessTokenVersionTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AccessToken_BecomesInvalid_AfterPasswordChange_IncrementsTokenVersion()
    {
        var client = _factory.CreateClient();

        async Task<HttpResponseMessage> PostJson(string path, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var resp = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
            return resp;
        }

        const string email = "test@example.com";
        const string initialPassword = "Password123!";

        // Create user via signup
        var signupResp = await PostJson("/api/auth/signup", new { email, password = initialPassword });
        signupResp.EnsureSuccessStatusCode();

        // Login to get access token (endpoint: /api/auth/login)
        var loginResp = await PostJson("/api/auth/login", new { email, password = initialPassword });
        loginResp.EnsureSuccessStatusCode();
        using var loginJson = await JsonDocument.ParseAsync(await loginResp.Content.ReadAsStreamAsync());
        var accessToken = loginJson.RootElement.GetProperty("access").GetProperty("token").GetString();
        accessToken.Should().NotBeNull();

        // Call /api/me successfully with current token
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meResp = await client.SendAsync(req);
        meResp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // Change password (increments TokenVersion) endpoint: /api/auth/change-password (requires auth header)
        var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { currentPassword = initialPassword, newPassword = "NewPassword456!" }), Encoding.UTF8, "application/json")
        };
        changeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var changeResp = await client.SendAsync(changeReq);
        changeResp.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Old token should now be invalid
        var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meResp2 = await client.SendAsync(req2);
        meResp2.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
