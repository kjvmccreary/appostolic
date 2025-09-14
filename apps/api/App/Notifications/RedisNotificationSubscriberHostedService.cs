using StackExchange.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Notifications;

public sealed class RedisNotificationSubscriberHostedService : IHostedService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly INotificationIdQueue _idQueue;
    private readonly string _channel;
    private readonly ILogger<RedisNotificationSubscriberHostedService> _logger;
    private readonly RedisTransportDiagnostics _diagnostics;
    private ISubscriber? _subscriber;

    public RedisNotificationSubscriberHostedService(
        IConnectionMultiplexer redis,
        INotificationIdQueue idQueue,
        IOptions<App.Options.NotificationTransportOptions> transportOptions,
        RedisTransportDiagnostics diagnostics,
        ILogger<RedisNotificationSubscriberHostedService> logger)
    {
        _redis = redis;
        _idQueue = idQueue;
        _channel = transportOptions.Value.Redis.Channel;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriber = _redis.GetSubscriber();
        _diagnostics.Enabled = true;
        _diagnostics.Channel = _channel;
    await _subscriber.SubscribeAsync(RedisChannel.Literal(_channel), async (channel, message) =>
        {
            try
            {
                if (Guid.TryParse(message, out var id))
                {
                    await _idQueue.EnqueueAsync(id, cancellationToken).ConfigureAwait(false);
                    _diagnostics.NoticeMessage();
                    _logger.LogDebug("Forwarded notification {NotificationId} from Redis to in-process queue", id);
                }
                else
                {
                    _logger.LogWarning("Received non-GUID payload on Redis channel {Channel}: {Payload}", channel.ToString(), (string)message!);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Redis Pub/Sub message: {Payload}", (string)message!);
            }
        }).ConfigureAwait(false);
        _diagnostics.Subscribed = true;
        _logger.LogInformation("Subscribed to Redis channel {Channel} for notification IDs", _channel);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber != null)
        {
            await _subscriber.UnsubscribeAsync(RedisChannel.Literal(_channel)).ConfigureAwait(false);
            _diagnostics.Subscribed = false;
            _logger.LogInformation("Unsubscribed from Redis channel {Channel}", _channel);
        }
    }
}
