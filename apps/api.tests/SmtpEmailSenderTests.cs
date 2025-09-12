using System.Net.Mail;
using System.Threading;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task Sends_with_both_text_and_html()
    {
        var fakeClient = new FakeSmtpClient();
        var factory = new FakeFactory(fakeClient);
        var emailOptions = Options.Create(new EmailOptions { FromAddress = "no-reply@test.local", FromName = "Test" });
        var sender = new SmtpEmailSender(factory, emailOptions, NullLogger<SmtpEmailSender>.Instance);

        await sender.SendAsync("to@example.com", "Hello", "<b>Hi</b>", "Hi");

        Assert.True(fakeClient.Sent);
        Assert.Equal("Hello", fakeClient.LastMessage?.Subject);
        Assert.NotNull(fakeClient.LastMessage);
        Assert.Equal(2, fakeClient.LastMessage!.AlternateViews.Count); // text + html
    }

    private sealed class FakeFactory : ISmtpClientFactory
    {
        private readonly SmtpClient _client;
        public FakeFactory(SmtpClient client) => _client = client;
        public SmtpClient Create() => _client;
    }

    private sealed class FakeSmtpClient : SmtpClient
    {
        public bool Sent { get; private set; }
        public MailMessage? LastMessage { get; private set; }

        public override Task SendMailAsync(MailMessage message, CancellationToken cancellationToken)
        {
            Sent = true;
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
