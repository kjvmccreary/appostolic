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
