using System.Collections.Generic;
using Appostolic.Api.Application.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Privacy;

public class LoggingPIIScopeTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<IDictionary<string, object?>> _scopes = new();
        public IReadOnlyList<IDictionary<string, object?>> Scopes => _scopes;
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_scopes);
        public void Dispose() { }
        private sealed class CapturingLogger : ILogger
        {
            private readonly List<IDictionary<string, object?>> _scopes;
            public CapturingLogger(List<IDictionary<string, object?>> scopes) => _scopes = scopes;
            public IDisposable BeginScope<TState>(TState state)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var kv in kvps)
                        dict[kv.Key] = kv.Value;
                    _scopes.Add(dict);
                }
                return NullScope.Instance;
            }
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter) { }
            private sealed class NullScope : System.IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }

    private static (ILogger Logger, CapturingLoggerProvider Provider, IPIIHasher Hasher, IOptions<PrivacyOptions> Opts) Create(bool hashingEnabled = true)
    {
        var provider = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(b => b.AddProvider(provider));
        var logger = loggerFactory.CreateLogger("Test");
        var opts = Options.Create(new PrivacyOptions
        {
            PIIHashingEnabled = hashingEnabled,
            PIIHashPepper = "pepper" // deterministic for test
        });
        var hasher = new Sha256PIIHasher(opts);
        return (logger, provider, hasher, opts);
    }

    [Fact]
    public void EmailScope_IncludesRedactedAndHash_WhenEnabled()
    {
        var (logger, provider, hasher, opts) = Create(true);
        using (LoggingPIIScope.BeginEmailScope(logger, "User@example.com", hasher, opts)) { }
        var scope = Assert.Single(provider.Scopes);
    // Redactor preserves original first character casing ("User@example.com" -> "U***@example.com")
    Assert.Equal("U***@example.com", scope["user.email.redacted"]);
        Assert.True(scope.ContainsKey("user.email.hash"));
        Assert.DoesNotContain(scope.Keys, k => k == "user.email.raw");
    }

    [Fact]
    public void EmailScope_OnlyRedacted_WhenHashingDisabled()
    {
        var (logger, provider, hasher, opts) = Create(false);
        using (LoggingPIIScope.BeginEmailScope(logger, "user@example.com", hasher, opts)) { }
        var scope = Assert.Single(provider.Scopes);
    Assert.Equal("u***@example.com", scope["user.email.redacted"]);
        Assert.False(scope.ContainsKey("user.email.hash"));
    }

    [Fact]
    public void PhoneScope_IncludesRedactedAndHash_WhenEnabled()
    {
        var (logger, provider, hasher, opts) = Create(true);
        using (LoggingPIIScope.BeginPhoneScope(logger, "+1 (555) 123-4567", hasher, opts)) { }
        var scope = Assert.Single(provider.Scopes);
        Assert.Equal("***4567", scope["user.phone.redacted"]);
        Assert.True(scope.ContainsKey("user.phone.hash"));
    }

    [Fact]
    public void PhoneScope_OnlyRedacted_WhenDisabled()
    {
        var (logger, provider, hasher, opts) = Create(false);
        using (LoggingPIIScope.BeginPhoneScope(logger, "5551234567", hasher, opts)) { }
        var scope = Assert.Single(provider.Scopes);
        Assert.Equal("***4567", scope["user.phone.redacted"]);
        Assert.False(scope.ContainsKey("user.phone.hash"));
    }
}
