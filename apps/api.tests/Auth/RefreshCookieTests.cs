using System.Net;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Net.Http;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class RefreshCookieTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshCookieTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_Sets_Refresh_Cookie_When_Flag_Enabled()
    {
        var client = _factory.CreateClient();
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
        var client = _factory.CreateClient();
        var email = $"rotuser-{Guid.NewGuid():N}@example.com";
    var signup2 = await client.PostAsJsonAsync("/api/auth/signup", new { email, password = "P@ssw0rd!1" });
    var signup = signup2; // preserve variable name usage below
        signup.EnsureSuccessStatusCode();

    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "P@ssw0rd!1" });
        login.EnsureSuccessStatusCode();
        login.Headers.TryGetValues("Set-Cookie", out var firstCookies).Should().BeTrue();
    var firstRt = firstCookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase)).Split(';')[0];

        // Extract refresh token from body for select-tenant
        var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
        var refreshToken = body.GetProperty("refresh").GetProperty("token").GetString();
        refreshToken.Should().NotBeNull();
        // Extract tenant slug from memberships (personal tenant slug is returned via signup->login membership projection)
        var memberships = body.GetProperty("memberships").EnumerateArray().ToList();
        memberships.Should().HaveCount(1);
        var tenantSlug = memberships[0].GetProperty("tenantSlug").GetString();
        tenantSlug.Should().NotBeNull();

    var selectResp = await client.PostAsJsonAsync("/api/auth/select-tenant", new { tenant = tenantSlug, refreshToken });
        selectResp.EnsureSuccessStatusCode();
        selectResp.Headers.TryGetValues("Set-Cookie", out var rotatedCookies).Should().BeTrue();
    var secondRt = rotatedCookies!.First(c => c.StartsWith("rt=", StringComparison.OrdinalIgnoreCase)).Split(';')[0];
        secondRt.Should().NotBe(firstRt, "rotation should issue new cookie value");
    }
}
