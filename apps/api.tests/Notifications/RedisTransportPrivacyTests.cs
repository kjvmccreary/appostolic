using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class RedisTransportPrivacyTests
{
    [Fact]
    public async Task NonGuidPayload_DoesNotLogPayloadContents()
    {
        // Arrange
        var channelName = "app:notifications:queued";
    Action<RedisChannel, RedisValue>? capturedHandler = null;

        var sub = new Mock<ISubscriber>();
        sub.Setup(s => s.SubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, handler, flags) =>
            {
                capturedHandler = handler;
            })
            .Returns(Task.CompletedTask);

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(sub.Object);

        var queue = new Mock<INotificationIdQueue>(MockBehavior.Strict);
        var diagnostics = new RedisTransportDiagnostics();
        var options = Options.Create(new NotificationTransportOptions
        {
            Redis = new RedisTransportOptions { Channel = channelName }
        });

        using var logger = new FakeLogger<RedisNotificationSubscriberHostedService>();
        var service = new RedisNotificationSubscriberHostedService(mux.Object, queue.Object, options, diagnostics, logger);

        // Act
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedHandler); // sanity

        var rawPayload = "not-a-guid-secret";
    capturedHandler!(new RedisChannel(channelName, RedisChannel.PatternMode.Literal), rawPayload);
    await Task.Delay(10); // allow async-void handler to flush logs

        // Assert: no log message should contain the raw payload contents
        var messages = logger.Logs.Select(l => l.message).ToArray();
        Assert.True(messages.Length > 0, "Expected at least one log message to be written.");
        Assert.DoesNotContain(messages, m => m.Contains(rawPayload, StringComparison.Ordinal));

        // And warning should include channel and payload length, not contents
        Assert.Contains(messages, m => m.Contains("payload_length=", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains(channelName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnqueueFailure_LogsErrorWithoutPayloadContents()
    {
        // Arrange
        var channelName = "app:notifications:queued";
    Action<RedisChannel, RedisValue>? capturedHandler = null;

        var sub = new Mock<ISubscriber>();
        sub.Setup(s => s.SubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((ch, handler, flags) =>
            {
                capturedHandler = handler;
            })
            .Returns(Task.CompletedTask);

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(sub.Object);

        var queue = new Mock<INotificationIdQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("boom"));

        var diagnostics = new RedisTransportDiagnostics();
        var options = Options.Create(new NotificationTransportOptions
        {
            Redis = new RedisTransportOptions { Channel = channelName }
        });

        using var logger = new FakeLogger<RedisNotificationSubscriberHostedService>();
        var service = new RedisNotificationSubscriberHostedService(mux.Object, queue.Object, options, diagnostics, logger);

        // Act
        await service.StartAsync(CancellationToken.None);
        Assert.NotNull(capturedHandler); // sanity

        var guidPayload = Guid.NewGuid().ToString("D");
    capturedHandler!(new RedisChannel(channelName, RedisChannel.PatternMode.Literal), guidPayload);
    await Task.Delay(10); // allow async-void handler to execute catch/log

        // Assert: error logs should not include payload contents (the GUID)
        var messages = logger.Logs
            .Where(l => l.level >= LogLevel.Warning)
            .Select(l => l.message)
            .ToArray();
        Assert.True(messages.Length > 0, "Expected warning/error logs to be written.");
        Assert.DoesNotContain(messages, m => m.Contains(guidPayload, StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains(channelName, StringComparison.Ordinal));
    }
}
