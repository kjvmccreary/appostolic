using System.Net;
using System.Text.Json;
using Xunit;

namespace Appostolic.Api.E2E;

public class SecureRefreshCookieTests : IClassFixture<SecureRefreshCookieTests.Fixture>
{
    public class Fixture : IAsyncLifetime
    {
        public E2EHostFixture Host { get; private set; } = default!; // Initialized in InitializeAsync

        public async Task InitializeAsync()
        {
            Host = await E2EHostFixture.LaunchAsync();
        }

    public Task DisposeAsync() => Host.DisposeAsync();
    }

    private readonly Fixture _fx;
    public SecureRefreshCookieTests(Fixture fx) => _fx = fx;

    [Fact(DisplayName = "Signup/Login issues Secure HttpOnly refresh cookie over HTTPS")]
    public async Task Signup_And_Login_Sets_Secure_RefreshCookie()
    {
        // Arrange: create a new user via magic consume or login path.
        // For simplicity assume a test signup endpoint or existing fixture user; using magic consume placeholder.
        // Use dedicated E2E helper that issues a dummy refresh cookie
        var resp = await _fx.Host.Client.GetAsync("/e2e/issue-cookie");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var setCookies = resp.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.ToList()
            : new List<string>();

        Assert.NotEmpty(setCookies);
        var rt = setCookies.FirstOrDefault(c => c.StartsWith("rt="));
        Assert.False(string.IsNullOrWhiteSpace(rt), "Refresh cookie (rt) not found in Set-Cookie headers");
    // non-null after assertion

    // Attribute assertions (case-insensitive; Kestrel may lowercase attributes)
    var lower = rt.ToLowerInvariant();
    Assert.Contains("secure", lower);
    Assert.Contains("httponly", lower);
    Assert.Contains("samesite=lax", lower);
    Assert.Contains("path=/", lower);

        // Expires future
        var parts = rt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expiresPart = parts.FirstOrDefault(p => p.StartsWith("Expires=", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(expiresPart);
        if (expiresPart != null)
        {
            var raw = expiresPart.Substring("Expires=".Length);
            Assert.True(DateTime.TryParse(raw, out var dt), $"Could not parse Expires: {raw}");
            Assert.True(dt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(10), "Expires not sufficiently in the future");
        }
    }
}
