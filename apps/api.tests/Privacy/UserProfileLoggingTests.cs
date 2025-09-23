using System.Net.Http.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using Appostolic.Api.Application.Privacy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Privacy;

public class UserProfileLoggingTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public UserProfileLoggingTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetMe_EmitsRedactedAndHash_NoRawEmail()
    {
        var provider = new CapturingLoggerProvider();
        var customFactory = new LoggingWebAppFactory(provider, hashingEnabled: true);
        var client = customFactory.CreateClient();

        // Deterministic token issuance for kevin@example.com (preserves existing redaction expectation)
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(customFactory, "kevin@example.com", $"privacy-{Guid.NewGuid():N}".Substring(0, 20), owner: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/users/me");
        resp.EnsureSuccessStatusCode();
        var scopes = provider.Scopes;
        Assert.Contains(scopes, s => s.ContainsKey("user.email.redacted") && (string?)s["user.email.redacted"] == "k***@example.com");
        Assert.Contains(scopes, s => s.ContainsKey("user.email.hash"));
        Assert.DoesNotContain(scopes, s => s.ContainsKey("user.email.raw"));
        Assert.DoesNotContain(scopes, s => s.Any(kv => kv.Value is string sv && sv == "kevin@example.com"));
    }

    [Fact]
    public async Task GetMe_HashingDisabled_OmitsHash()
    {
        var provider = new CapturingLoggerProvider();
        var customFactory2 = new LoggingWebAppFactory(provider, hashingEnabled: false);
        var client = customFactory2.CreateClient();
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(customFactory2, "kevin@example.com", $"privacy-{Guid.NewGuid():N}".Substring(0, 20), owner: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/users/me");
        resp.EnsureSuccessStatusCode();
        var scopes = provider.Scopes;
        var scope = Assert.Single(scopes.Where(s => s.ContainsKey("user.email.redacted")));
        Assert.False(scope.ContainsKey("user.email.hash"));
    }

    // Local capturing logger provider mirroring pattern from EmailDispatcherTests
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<IDictionary<string, object?>> _scopes = new();
        public IReadOnlyList<IDictionary<string, object?>> Scopes => _scopes;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_scopes);
        public void Dispose() { }
        private sealed class CapturingLogger : ILogger
        {
            private readonly List<IDictionary<string, object?>> _store;
            public CapturingLogger(List<IDictionary<string, object?>> store) => _store = store;
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var kv in kvps) dict[kv.Key] = kv.Value;
                    _store.Add(dict);
                }
                return NullScope.Instance;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }

    /// <summary>
    /// Test-only factory injecting a capturing logger provider and configuring PrivacyOptions hashing flag
    /// while retaining the concrete WebAppFactory type required by AuthTestClientFlow helpers.
    /// </summary>
    private sealed class LoggingWebAppFactory : WebAppFactory
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly bool _hashingEnabled;
        public LoggingWebAppFactory(CapturingLoggerProvider provider, bool hashingEnabled)
        {
            _provider = provider; _hashingEnabled = hashingEnabled;
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddLogging(b => b.AddProvider(_provider));
                services.PostConfigure<PrivacyOptions>(o => { o.PIIHashingEnabled = _hashingEnabled; o.PIIHashPepper = "pep"; });
            });
        }
    }
}
