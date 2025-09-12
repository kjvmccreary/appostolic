using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.App.Notifications;

public sealed class EmailDispatcherHostedService : BackgroundService
{
    private readonly ILogger<EmailDispatcherHostedService> _logger;
    private readonly IEmailQueue _queue;
    private readonly ITemplateRenderer _renderer;
    private readonly IEmailSender _sender;

    public EmailDispatcherHostedService(
        ILogger<EmailDispatcherHostedService> logger,
        IEmailQueue queue,
        ITemplateRenderer renderer,
        IEmailSender sender)
    {
        _logger = logger;
        _queue = queue;
        _renderer = renderer;
        _sender = sender;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EmailDispatcher started");

        var reader = _queue.Reader;
        while (!stoppingToken.IsCancellationRequested)
        {
            EmailMessage msg;
            try
            {
                msg = await reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Basic retry with backoff: 3 attempts 0.5s, 2s, 8s
            var delays = new[] { 500, 2000, 8000 };
            var attempt = 0;
            Exception? last = null;

            var scopeState = new Dictionary<string, object?>
            {
                ["email.kind"] = msg.Kind.ToString(),
                ["email.to"] = msg.ToEmail
            };

            // Optional correlation fields if provided by the producer
            if (msg.Data is { } data && data.Count > 0)
            {
                void AddIfPresent(string dataKey, string scopeKey)
                {
                    if (data.TryGetValue(dataKey, out var v) && v is not null)
                    {
                        scopeState[scopeKey] = v;
                    }
                }

                AddIfPresent("userId", "email.userId");
                AddIfPresent("tenantId", "email.tenantId");
                AddIfPresent("inviteId", "email.inviteId");

                // Fallback human-friendly fields
                AddIfPresent("tenant", "email.tenant");
                AddIfPresent("inviter", "email.inviter");
            }

            using var scope = _logger.BeginScope(scopeState);

            for (; attempt < delays.Length; attempt++)
            {
                try
                {
                    var (subject, html, text) = await _renderer.RenderAsync(msg, stoppingToken);
                    await _sender.SendAsync(msg.ToEmail, subject, html, text, stoppingToken);
                    EmailMetrics.RecordSent(msg.Kind.ToString());
                    _logger.LogInformation("Email sent");
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
                    _logger.LogWarning(ex, "Email send failed on attempt {Attempt}", attempt + 1);
                    if (attempt < delays.Length - 1)
                    {
                        try
                        {
                            await Task.Delay(delays[attempt], stoppingToken);
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
                EmailMetrics.RecordFailed(msg.Kind.ToString());
                _logger.LogError(last, "Email permanently failed after {Attempts} attempts", attempt);
            }
        }

        _logger.LogInformation("EmailDispatcher stopping");
    }
}
