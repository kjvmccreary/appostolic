using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Api;

public class AuthJwtSmokeTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AuthJwtSmokeTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AuthSmoke_Ping_WithIssuedToken_Succeeds()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Infrastructure.Auth.Jwt.IJwtTokenService>();
        // Provide tokenVersion = 0 for test issued token
        var token = jwt.IssueNeutralToken("test-user", 0, "test@example.com");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var resp = await client.GetAsync("/auth-smoke/ping");
        var body = await resp.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("test-user", body);
    }
}
