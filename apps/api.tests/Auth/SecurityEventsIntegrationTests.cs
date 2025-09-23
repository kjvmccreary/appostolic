using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 6: Integration tests verifying that structured security events are emitted for key auth flows.
/// Captures ILogger "Security.Auth" category output via a test logger provider injected into the WebAppFactory.
/// Validates JSON envelope (v=1, type, reason, user_id presence when expected) for representative event types.
/// </summary>
public class SecurityEventsIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SecurityEventsIntegrationTests(WebAppFactory factory) => _factory = factory;

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public readonly List<string> Lines = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Lines);
        public void Dispose() { }
        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<string> _lines;
            public CapturingLogger(string category, List<string> lines) { _category = category; _lines = lines; }
            IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (_category == "Security.Auth")
                {
                    try { _lines.Add(formatter(state, exception)); } catch { /* swallow */ }
                }
            }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithCaptured(out CapturingLoggerProvider provider)
    {
        var cap = new CapturingLoggerProvider();
        provider = cap;
        var baseFactory = _factory.WithSettings(new Dictionary<string,string?>
        {
            ["AUTH__SECURITY_EVENTS__ENABLED"] = "true"
        });
        return baseFactory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(s =>
            {
                var p = cap; // local copy for closure
                s.AddLogging(lb => lb.AddProvider(p));
            });
        });
    }

    [Fact]
    public async Task Emits_Login_Failure_Event()
    {
    var factory = WithCaptured(out var provider);
        var client = factory.CreateClient();
        // Missing fields -> login_failure reason=missing_fields
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // Examine captured lines
        provider.Lines.Should().NotBeEmpty();
        var evt = provider.Lines
            .Select(ExtractJson)
            .First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="login_failure");
        evt!.RootElement.GetProperty("v").GetInt32().Should().Be(1);
        evt.RootElement.GetProperty("reason").GetString().Should().Be("missing_fields");
    }

    [Fact]
    public async Task Emits_Refresh_Reuse_Event()
    {
        // Configure small TTL to simplify reuse path (reuse occurs when revoked token used again).
    var factory = WithCaptured(out var provider);
        var email = $"secevt_{Guid.NewGuid()}@test.com";
        var password = "Password123!";
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/signup", new { email, password });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        login.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var rtCookie = cookies!.First(c=>c.StartsWith("rt="));
        string cookieHeader = rtCookie.Split(';')[0];
        // First refresh succeeds and rotates token, making old token revoked -> reuse attempt using old cookie should produce refresh_reuse event.
    var firstReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
    firstReq.Headers.Add("Cookie", cookieHeader);
    firstReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
    var first = await client.SendAsync(firstReq);
    first.IsSuccessStatusCode.Should().BeTrue();
    // New client without cookie container state to avoid sending rotated cookie
    var reuseClient = factory.CreateClient();
    var reuseReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
    reuseReq.Headers.Add("Cookie", cookieHeader); // intentionally reuse old cookie
    reuseReq.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
    var reuse = await reuseClient.SendAsync(reuseReq);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        provider.Lines.Any(l => l.Contains("refresh_reuse")).Should().BeTrue();
        var evt = provider.Lines.Select(ExtractJson).First(e => e != null && e.RootElement.TryGetProperty("type", out var t) && t.GetString()=="refresh_reuse");
        evt!.RootElement.GetProperty("user_id").GetGuid(); // ensures guid parse ok
        evt.RootElement.GetProperty("refresh_id").GetGuid();
        evt.RootElement.GetProperty("reason").GetString().Should().Be("refresh_reuse");
    }

    private static JsonDocument? ExtractJson(string line)
    {
        var idx = line.IndexOf('{');
        if (idx < 0) return null;
        var json = line.Substring(idx);
        try { return JsonDocument.Parse(json); } catch { return null; }
    }
}
