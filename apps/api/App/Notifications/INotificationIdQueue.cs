using System.Threading.Channels;

namespace Appostolic.Api.App.Notifications;

public interface INotificationIdQueue
{
    ValueTask EnqueueAsync(Guid notificationId, CancellationToken ct = default);
    ChannelReader<Guid> Reader { get; }
}

public sealed class NotificationIdQueue : INotificationIdQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Guid notificationId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(notificationId, ct);
}
