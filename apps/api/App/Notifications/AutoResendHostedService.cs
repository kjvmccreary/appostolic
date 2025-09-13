using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.App.Notifications;

public sealed class AutoResendHostedService : BackgroundService
{
    private readonly ILogger<AutoResendHostedService> _logger;
    private readonly IAutoResendScanner _scanner;
    private readonly Appostolic.Api.App.Options.NotificationOptions _options;

    public AutoResendHostedService(ILogger<AutoResendHostedService> logger,
        IAutoResendScanner scanner,
        Microsoft.Extensions.Options.IOptions<Appostolic.Api.App.Options.NotificationOptions> options)
    {
        _logger = logger;
        _scanner = scanner;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableAutoResend)
        {
            _logger.LogInformation("AutoResend is disabled by configuration");
            return;
        }

        _logger.LogInformation("AutoResendHostedService started with interval {Interval}", _options.AutoResendScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _scanner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AutoResend scan iteration failed");
            }

            try
            {
                await Task.Delay(_options.AutoResendScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AutoResendHostedService stopping");
    }
}
