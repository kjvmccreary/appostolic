using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Appostolic.Api.Tests.Auth;

public class SignupTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public SignupTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Signup_SelfServe_CreatesUserAndPersonalTenant()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"tester{Guid.NewGuid():N}@example.com";
        var payload = new { email, password = "P@ssw0rd!" };

        // Act
        var resp = await client.PostAsJsonAsync("/api/auth/signup", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        doc.TryGetProperty("id", out _).Should().BeTrue();
        doc.GetProperty("email").GetString().Should().Be(email);
        doc.TryGetProperty("tenant", out var t).Should().BeTrue();
        var name = t.GetProperty("name").GetString();
        name.Should().NotBeNull();
        name!.Should().EndWith("-personal");
    }

    [Fact]
    public async Task Signup_WithInvalidInviteToken_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"inv{Guid.NewGuid():N}@example.com";
        var payload = new { email, password = "P@ssw0rd!", inviteToken = "not-a-real-token" };

        // Act
        var resp = await client.PostAsJsonAsync("/api/auth/signup", payload);

        // Assert
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await resp.Content.ReadAsStringAsync();
        text.Should().Contain("invalid or expired invite");
    }
}
