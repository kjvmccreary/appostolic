using System.Threading.Channels;
using Appostolic.Api.App.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Appostolic.Api.Tests;

public class EmailDedupeTests
{
    [Fact]
    public async Task Dedupe_suppresses_duplicate_messages()
    {
        var sends = 0;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmailQueue, EmailQueue>();
        services.AddSingleton<ITemplateRenderer, FakeRenderer>();
        services.AddSingleton<IEmailSender>(sp => new CountingSender(() => sends++));
        services.AddSingleton<IEmailDedupeStore, InMemoryEmailDedupeStore>();
        services.AddHostedService<EmailDispatcherHostedService>();

        await using var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IHostedService>();
        await host.StartAsync(CancellationToken.None);

        var queue = provider.GetRequiredService<IEmailQueue>();
        var data = new Dictionary<string, object?>();
        var msg1 = new EmailMessage(EmailKind.Verification, "user@example.com", null, data, "k1");
        var msg2 = new EmailMessage(EmailKind.Verification, "user@example.com", null, data, "k1");

        await queue.EnqueueAsync(msg1);
        await queue.EnqueueAsync(msg2);

        // wait a bit for processing
        await Task.Delay(300);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal(1, sends);
    }

    private sealed class CountingSender : IEmailSender
    {
        private readonly Action _inc;
        public CountingSender(Action inc) => _inc = inc;
        public Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
        {
            _inc();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRenderer : ITemplateRenderer
    {
        public Task<(string Subject, string Html, string Text)> RenderAsync(EmailMessage message, CancellationToken ct = default)
            => Task.FromResult<(string, string, string)>(($"[{message.Kind}]", "<b>body</b>", "body"));
    }
}
