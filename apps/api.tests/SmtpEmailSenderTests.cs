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
    Assert.Equal("Hello", fakeClient.LastSubject);
    Assert.Equal(2, fakeClient.LastAlternateViewCount); // text + html
    }

    private sealed class FakeFactory : ISmtpClientFactory
    {
        private readonly IAsyncSmtpClient _client;
        public FakeFactory(IAsyncSmtpClient client) => _client = client;
        public IAsyncSmtpClient Create() => _client;
    }

    private sealed class FakeSmtpClient : IAsyncSmtpClient
    {
        public bool Sent { get; private set; }
        public string? LastSubject { get; private set; }
        public int LastAlternateViewCount { get; private set; }
        public void Dispose() { }
        public Task SendMailAsync(MailMessage message, CancellationToken cancellationToken = default)
        {
            Sent = true;
            LastSubject = message.Subject;
            LastAlternateViewCount = message.AlternateViews.Count;
            return Task.CompletedTask;
        }
    }
}
