using System.Threading.Channels;
using Appostolic.Api.App.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests;

public class EmailDispatcherTests
{
    [Fact]
    public async Task Enqueue_sends_via_sender()
    {
        var sent = new TaskCompletionSource<(string To, string Subject)>();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        services.AddSingleton<IEmailQueue, EmailQueue>();
        services.AddSingleton<ITemplateRenderer, FakeRenderer>();
        services.AddSingleton<IEmailSender>(sp => new CapturingSender(sent));
        services.AddHostedService<EmailDispatcherHostedService>();
        services.Configure<Appostolic.Api.App.Options.EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IHostedService>();
        var cts = new CancellationTokenSource();
        await host.StartAsync(cts.Token);

        var queue = provider.GetRequiredService<IEmailQueue>();
        await queue.EnqueueAsync(new EmailMessage(EmailKind.Verification, "user@example.com", null, new Dictionary<string, object?>()));

        var (to, subject) = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("user@example.com", to);
        Assert.Contains("Verify", subject);

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dispatcher_logs_correlation_fields_when_present()
    {
        var sent = new TaskCompletionSource<(string To, string Subject)>();

        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(loggerProvider));
        services.AddSingleton<IEmailQueue, EmailQueue>();
        services.AddSingleton<ITemplateRenderer, FakeRenderer>();
        services.AddSingleton<IEmailSender>(sp => new CapturingSender(sent));
        services.AddHostedService<EmailDispatcherHostedService>();
        services.Configure<Appostolic.Api.App.Options.EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IHostedService>();
        var cts = new CancellationTokenSource();
        await host.StartAsync(cts.Token);

        var queue = provider.GetRequiredService<IEmailQueue>();
        var data = new Dictionary<string, object?>
        {
            ["userId"] = "u1",
            ["tenantId"] = "t1",
            ["inviteId"] = "i1",
            ["tenant"] = "Acme",
            ["inviter"] = "Alice"
        };
        await queue.EnqueueAsync(new EmailMessage(EmailKind.Invite, "user@example.com", null, data));

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Verify scope captured expected keys
        var scopes = loggerProvider.Scopes;
        Assert.Contains(scopes, s => s.ContainsKey("email.userId") && (string?)s["email.userId"] == "u1");
        Assert.Contains(scopes, s => s.ContainsKey("email.tenantId") && (string?)s["email.tenantId"] == "t1");
        Assert.Contains(scopes, s => s.ContainsKey("email.inviteId") && (string?)s["email.inviteId"] == "i1");
        Assert.Contains(scopes, s => s.ContainsKey("email.tenant") && (string?)s["email.tenant"] == "Acme");
        Assert.Contains(scopes, s => s.ContainsKey("email.inviter") && (string?)s["email.inviter"] == "Alice");

        await host.StopAsync(CancellationToken.None);
    }

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

            IDisposable ILogger.BeginScope<TState>(TState state)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
                {
                    var dict = new Dictionary<string, object?>();
                    foreach (var kv in kvps)
                    {
                        dict[kv.Key] = kv.Value;
                    }
                    _scopes.Add(dict);
                }
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }

    private sealed class FakeRenderer : ITemplateRenderer
    {
        public Task<(string Subject, string Html, string Text)> RenderAsync(EmailMessage msg, CancellationToken ct)
            => Task.FromResult(((string Subject, string Html, string Text))("Verify your email", "<b>verify</b>", "verify"));
    }

    private sealed class CapturingSender : IEmailSender
    {
        private readonly TaskCompletionSource<(string To, string Subject)> _tcs;
        public CapturingSender(TaskCompletionSource<(string To, string Subject)> tcs) => _tcs = tcs;

        public Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
        {
            _tcs.TrySetResult((toEmail, subject));
            return Task.CompletedTask;
        }
    }
}
