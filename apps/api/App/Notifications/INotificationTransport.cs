namespace Appostolic.Api.App.Notifications;

/// <summary>
/// Abstraction over the notifications transport used to wake a dispatcher or publish
/// a queued-notification event. Initial implementation delegates to the existing
/// in-process ID queue for no behavior change.
/// </summary>
public interface INotificationTransport
{
    /// <summary>
    /// Publish a signal that a notification with the specified outbox id has been queued.
    /// Implementations must be idempotent and lightweight.
    /// </summary>
    ValueTask PublishQueuedAsync(Guid notificationId, CancellationToken ct = default);
}

/// <summary>
/// Channel-based transport that uses the existing in-memory ID queue to wake the dispatcher.
/// </summary>
public sealed class ChannelNotificationTransport : INotificationTransport
{
    private readonly INotificationIdQueue _idQueue;

    public ChannelNotificationTransport(INotificationIdQueue idQueue)
    {
        _idQueue = idQueue;
    }

    public ValueTask PublishQueuedAsync(Guid notificationId, CancellationToken ct = default)
        => _idQueue.EnqueueAsync(notificationId, ct);
}
