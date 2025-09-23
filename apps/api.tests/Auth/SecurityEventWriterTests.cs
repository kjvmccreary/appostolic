using Appostolic.Api.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 6: Tests for SecurityEventWriter schema & guards.
/// </summary>
public class SecurityEventWriterTests
{
    private ISecurityEventWriter CreateWriter(bool enabled = true)
    {
        var inMemory = new Dictionary<string,string?>
        {
            ["AUTH__SECURITY_EVENTS__ENABLED"] = enabled ? "true" : "false"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory!).Build();
        var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new TestLoggerProvider()));
        return new SecurityEventWriter(loggerFactory, config);
    }

    [Fact]
    public void Create_And_Emit_BasicEvent()
    {
        var writer = CreateWriter();
        var evt = writer.Create("login_failure", b => b.Reason("invalid_credentials"));
        writer.Emit(evt); // Should not throw
    }

    [Fact]
    public void Rejects_Unsupported_Type()
    {
        var writer = CreateWriter();
        Assert.Throws<ArgumentException>(() => writer.Create("weird_type", _ => { }));
    }

    [Fact]
    public void Rejects_Unsupported_Reason()
    {
        var writer = CreateWriter();
        Assert.Throws<ArgumentException>(() => writer.Create("login_failure", b => b.Reason("some_new_reason")));
    }

    [Fact]
    public void Pii_Guard_Detects_EmailLike()
    {
        var writer = CreateWriter();
        var evt = writer.Create("login_failure", b => b.Reason("invalid_credentials"));
        // Inject meta containing '@' to trigger guard
        evt.Meta!["suspicious"] = "user@example.com";
        Assert.Throws<InvalidOperationException>(() => writer.Emit(evt));
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger();
        public void Dispose() { }
        private class TestLogger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { /* swallow */ }
        }
        private class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
