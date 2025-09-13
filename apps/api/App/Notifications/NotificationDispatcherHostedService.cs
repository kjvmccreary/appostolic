using System.Text.Json;
using Appostolic.Api.Domain.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.App.Notifications;

public sealed class NotificationDispatcherHostedService : BackgroundService
{
    private readonly ILogger<NotificationDispatcherHostedService> _logger;
    private readonly IServiceProvider _sp;
    private readonly ITemplateRenderer _renderer;
    private readonly IEmailSender _sender;
    private readonly INotificationIdQueue _idQueue;

    // Retry/backoff policy: 3 attempts at ~0.5s, 2s, 8s (+/-20%), then dead-letter
    private static readonly int[] BaseDelaysMs = new[] { 500, 2000, 8000 };

    public NotificationDispatcherHostedService(
        ILogger<NotificationDispatcherHostedService> logger,
        IServiceProvider sp,
        ITemplateRenderer renderer,
        IEmailSender sender,
        INotificationIdQueue idQueue)
    {
        _logger = logger;
        _sp = sp;
        _renderer = renderer;
        _sender = sender;
        _idQueue = idQueue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDispatcher started");

        var reader = _idQueue.Reader;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Strategy: be event-driven via ID queue but also poll the DB when idle.
            // If queue provides an id, we still LeaseNextDueAsync to avoid double-send and honor schedule.
            try
            {
                // Wait for a signal or time out to poll periodically
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var readTask = reader.ReadAsync(stoppingToken).AsTask();
                var delayTask = Task.Delay(TimeSpan.FromSeconds(2), delayCts.Token);
                var completed = await Task.WhenAny(readTask, delayTask);

                // Cancel the other branch
                delayCts.Cancel();
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Process as many due notifications as available right now
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scopeSvc = _sp.CreateScope();
                var outbox = scopeSvc.ServiceProvider.GetRequiredService<INotificationOutbox>();
                var leased = await outbox.LeaseNextDueAsync(stoppingToken);
                if (leased is null) break;

                var scopeState = new Dictionary<string, object?>
                {
                    ["notification.id"] = leased.Id,
                    ["email.kind"] = leased.Kind.ToString(),
                    ["email.to"] = leased.ToEmail,
                };
                if (leased.TenantId is Guid tid)
                {
                    scopeState["email.tenantId"] = tid;
                }

                using var logScope = _logger.BeginScope(scopeState);

                var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(leased.DataJson) ?? new();
                Exception? last = null;
                var rand = Random.Shared;

                for (var attemptIndex = 0; attemptIndex < BaseDelaysMs.Length; attemptIndex++)
                {
                    try
                    {
                        var (subject, html, text) = await _renderer.RenderAsync(new EmailMessage(leased.Kind, leased.ToEmail, leased.ToName, data, leased.DedupeKey), stoppingToken);
                        await _sender.SendAsync(leased.ToEmail, subject, html, text, stoppingToken);
                        await outbox.MarkSentAsync(leased.Id, subject, html, text, stoppingToken);
                        _logger.LogInformation("Notification sent");
                        last = null;
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        _logger.LogWarning(ex, "Notification send failed on attempt {Attempt}", attemptIndex + 1);
                        if (attemptIndex < BaseDelaysMs.Length - 1)
                        {
                            var ms = BaseDelaysMs[attemptIndex];
                            var jitter = (int)(ms * 0.2);
                            var wait = ms + rand.Next(-jitter, jitter + 1);
                            var nextAt = DateTimeOffset.UtcNow.AddMilliseconds(wait);
                            await outbox.MarkFailedAsync(leased.Id, ex.Message, nextAt, stoppingToken);
                            try
                            {
                                await Task.Delay(wait, stoppingToken);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                        }
                    }
                }

                if (last != null)
                {
                    using var scope2 = _sp.CreateScope();
                    var outbox2 = scope2.ServiceProvider.GetRequiredService<INotificationOutbox>();
                    await outbox2.MarkDeadLetterAsync(leased.Id, last.Message, stoppingToken);
                    _logger.LogError(last, "Notification permanently failed");
                }
            }
        }

        _logger.LogInformation("NotificationDispatcher stopping");
    }
}
