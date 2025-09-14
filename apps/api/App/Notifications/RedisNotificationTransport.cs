using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Appostolic.Api.App.Notifications;

public sealed class RedisNotificationTransport : INotificationTransport
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _channel;
    private readonly ILogger<RedisNotificationTransport> _logger;

    public RedisNotificationTransport(
        IConnectionMultiplexer redis,
        IOptions<App.Options.NotificationTransportOptions> transportOptions,
        ILogger<RedisNotificationTransport> logger)
    {
        _redis = redis;
        _logger = logger;
        _channel = transportOptions.Value.Redis.Channel;
    }

    public async ValueTask PublishQueuedAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) return;
        var sub = _redis.GetSubscriber();
        var payload = notificationId.ToString("D");
        await sub.PublishAsync(_channel, payload).ConfigureAwait(false);
        _logger.LogDebug("Published notification {NotificationId} to Redis channel {Channel}", notificationId, _channel);
    }
}
