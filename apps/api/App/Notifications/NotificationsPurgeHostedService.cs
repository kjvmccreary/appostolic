using Appostolic.Api.App.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Notifications;

public sealed class NotificationsPurgeHostedService : BackgroundService
{
    private readonly ILogger<NotificationsPurgeHostedService> _logger;
    private readonly IServiceProvider _sp;
    private readonly NotificationOptions _options;
    private readonly INotificationsPurger _purger;

    public NotificationsPurgeHostedService(ILogger<NotificationsPurgeHostedService> logger, IServiceProvider sp, IOptions<NotificationOptions> options, INotificationsPurger purger)
    {
        _logger = logger;
        _sp = sp;
        _options = options.Value;
        _purger = purger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationsPurge started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                var result = await _purger.PurgeOnceAsync(db, now, _options, stoppingToken);
                if (result.TotalPurged > 0 || result.PiiScrubbed > 0)
                {
                    _logger.LogInformation("Notifications purge: dedupes={Expired}, sent={Sent}, failed={Failed}, dead={Dead}, scrubbed={Scrubbed}", result.DedupesPurged, result.SentPurged, result.FailedPurged, result.DeadPurged, result.PiiScrubbed);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during notifications purge");
            }

            // Run hourly
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { break; }
        }
        _logger.LogInformation("NotificationsPurge stopping");
    }
}
