using System.Threading;

namespace Appostolic.Api.App.Notifications;

/// <summary>
/// Lightweight in-memory diagnostics for the Redis notifications transport subscriber.
/// Used by dev health endpoints and a ping harness; safe to keep in Production (no PII).
/// </summary>
public sealed class RedisTransportDiagnostics
{
    private long _receivedCount;

    public bool Enabled { get; set; }
    public bool Subscribed { get; set; }
    public string Channel { get; set; } = "app:notifications:queued";
    public DateTimeOffset? LastReceivedAt { get; private set; }

    public long ReceivedCount => Interlocked.Read(ref _receivedCount);

    public void NoticeMessage()
    {
        Interlocked.Increment(ref _receivedCount);
        LastReceivedAt = DateTimeOffset.UtcNow;
    }
}
