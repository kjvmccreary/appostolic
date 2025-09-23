using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Additional Story 6 integration tests covering refresh_expired, refresh_rate_limited, and logout_all_user structured security events.
/// Reuses the logger capture approach from SecurityEventsIntegrationTests.
/// </summary>
public class SecurityEventsAdditionalIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SecurityEventsAdditionalIntegrationTests(WebAppFactory factory) => _factory = factory;

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly List<string> Lines = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Lines);
        public void Dispose() { }
        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category; private readonly List<string> _lines;
            public CapturingLogger(string category, List<string> lines) { _category = category; _lines = lines; }
            IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (_category == "Security.Auth")
                {
                    try { _lines.Add(formatter(state, exception)); } catch { }
                }
            }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithCaptured(out CapturingLoggerProvider provider, Dictionary<string,string?>? settings = null)
    {
        var cap = new CapturingLoggerProvider();
        provider = cap;
        var baseFactory = _factory.WithSettings(new Dictionary<string,string?>(settings ?? new())
        {
            ["AUTH__SECURITY_EVENTS__ENABLED"] = "true"
        });
        return baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddLogging(lb => lb.AddProvider(cap))));
    }

    private static JsonDocument? ExtractJson(string line)
    {
        var idx = line.IndexOf('{');
        if (idx < 0) return null;
        try { return JsonDocument.Parse(line.Substring(idx)); } catch { return null; }
    }

    [Fact]
    public async Task Emits_Refresh_Expired_Event()
    {
        // Use normal TTL then manually backdate the stored refresh token ExpiresAt to simulate expiration.
        var factory = WithCaptured(out var provider);
        var client = factory.CreateClient();
        var email = $"expired_{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        signup.EnsureSuccessStatusCode();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        // Extract refresh cookie plaintext token
        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var rtCookie = cookies!.First(c => c.StartsWith("rt="));
        var plaintext = rtCookie.Split(';')[0].Substring(3); // remove 'rt='
        // Backdate in DB
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = Appostolic.Api.Infrastructure.Auth.Jwt.RefreshTokenHashing.Hash(plaintext);
            var token = db.RefreshTokens.First(r => r.TokenHash == hash);
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            db.SaveChanges();
        }
        // Attempt refresh -> expect 401 refresh_expired and corresponding security event
        var refresh = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = JsonDocument.Parse(await refresh.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("refresh_expired");
        var evt = provider.Lines.Select(ExtractJson).First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="refresh_expired");
        evt.Should().NotBeNull();
        evt!.RootElement.GetProperty("reason").GetString().Should().Be("refresh_expired");
    }

    [Fact]
    public async Task Emits_Refresh_Rate_Limited_Event()
    {
        var factory = WithCaptured(out var provider, new Dictionary<string,string?>
        {
            ["AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS"] = "60",
            ["AUTH__REFRESH_RATE_LIMIT_MAX"] = "1",
            ["AUTH__REFRESH_RATE_LIMIT_DRY_RUN"] = "false"
        });
        var client = factory.CreateClient();
        var email = $"rlsecevt_{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        // First refresh ok
        var first = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        first.IsSuccessStatusCode.Should().BeTrue();
        // Second should 429
        var second = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        second.StatusCode.Should().Be((HttpStatusCode)429);
    var evt = provider.Lines.Select(ExtractJson).First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="refresh_rate_limited");
    evt.Should().NotBeNull();
    evt!.RootElement.GetProperty("reason").GetString().Should().Be("refresh_rate_limited");
    }

    [Fact]
    public async Task Emits_Logout_All_User_Event()
    {
        var factory = WithCaptured(out var provider);
        var client = factory.CreateClient();
        var email = $"logoutall_{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var access = loginJson.RootElement.GetProperty("access").GetProperty("token").GetString();
        var logoutAllReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout/all");
        logoutAllReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var logoutAll = await client.SendAsync(logoutAllReq);
        logoutAll.StatusCode.Should().Be(HttpStatusCode.NoContent);
    var evt = provider.Lines.Select(ExtractJson).First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="logout_all_user");
    evt.Should().NotBeNull();
    evt!.RootElement.GetProperty("reason").GetString().Should().Be("user_requested");
    }

    [Fact]
    public async Task Emits_Refresh_Invalid_Event()
    {
        // Supply a forged/unknown refresh token (cookie) and expect 401 refresh_invalid + security event.
        var factory = WithCaptured(out var provider);
        var client = factory.CreateClient();
        // Use a random token value unlikely to exist (no signup/login performed so DB empty of hashes)
        var forged = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "extra"; // ensure not valid base64 of a real issued token
        client.DefaultRequestHeaders.Add("Cookie", $"rt={forged}; Path=/; HttpOnly");
        var resp = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("code").GetString().Should().Be("refresh_invalid");
        // Security event (ip only) should be present
        var evt = provider.Lines.Select(ExtractJson).First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="refresh_invalid");
        evt.Should().NotBeNull();
        evt!.RootElement.GetProperty("reason").GetString().Should().Be("refresh_invalid");
    }

    [Fact]
    public async Task Emits_DryRun_Rate_Limited_Event_With_Meta()
    {
        // Configure limiter with max=1 and dry-run=true; second attempt should succeed (no 429) but emit security event with meta.dry_run=true.
        var factory = WithCaptured(out var provider, new Dictionary<string,string?>
        {
            ["AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS"] = "60",
            ["AUTH__REFRESH_RATE_LIMIT_MAX"] = "1",
            ["AUTH__REFRESH_RATE_LIMIT_DRY_RUN"] = "true"
        });
        var client = factory.CreateClient();
        var email = $"rldryevt_{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        // First refresh (allowed)
        var first = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        first.IsSuccessStatusCode.Should().BeTrue();
        // Second refresh would exceed max if not dry-run; should still be 200 but emit event with dry_run meta
        var second = await client.PostAsync("/api/auth/refresh", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        second.IsSuccessStatusCode.Should().BeTrue();
        var evt = provider.Lines.Select(ExtractJson).Last(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="refresh_rate_limited");
        evt.Should().NotBeNull();
        evt!.RootElement.GetProperty("reason").GetString().Should().Be("refresh_rate_limited");
        var meta = evt.RootElement.GetProperty("meta");
        meta.GetProperty("dry_run").GetString().Should().Be("true");
        meta.GetProperty("cause").GetString().Should().Be("window");
    }
}
