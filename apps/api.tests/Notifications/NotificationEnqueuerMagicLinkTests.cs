using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class NotificationEnqueuerMagicLinkTests
{
    [Fact]
    public async Task QueueMagicLink_HashesToken_And_Publishes()
    {
        var outbox = new Mock<INotificationOutbox>();
        var transport = new Mock<INotificationTransport>();
        var opts = Options.Create(new EmailOptions { WebBaseUrl = "http://localhost:3000" });
        var enq = new NotificationEnqueuer(outbox.Object, transport.Object, opts);

        Guid capturedId = Guid.Empty;
        outbox.Setup(o => o.CreateQueuedAsync(It.IsAny<EmailMessage>(), It.IsAny<string>(), It.IsAny<(string Subject, string Html, string Text)>(), default))
            .ReturnsAsync(() => { capturedId = Guid.NewGuid(); return capturedId; });
        transport.Setup(t => t.PublishQueuedAsync(It.IsAny<Guid>(), default))
            .Returns(new ValueTask());

        var token = "my-secret-token";
        await enq.QueueMagicLinkAsync("user@example.com", null, token);

        outbox.Verify(o => o.CreateQueuedAsync(
            It.Is<EmailMessage>(m => m.Kind == EmailKind.MagicLink),
            It.Is<string>(h => !string.IsNullOrWhiteSpace(h) && !string.Equals(h, token, StringComparison.Ordinal)),
            It.IsAny<(string, string, string)>(),
            default
        ), Times.Once);

        transport.Verify(t => t.PublishQueuedAsync(capturedId, default), Times.Once);
    }
}
