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
    private readonly IEmailDedupeStore? _dedupe;

    public EmailDispatcherHostedService(
        ILogger<EmailDispatcherHostedService> logger,
        IEmailQueue queue,
        ITemplateRenderer renderer,
        IEmailSender sender,
        IEmailDedupeStore? dedupe = null)
    {
        _logger = logger;
        _queue = queue;
        _renderer = renderer;
        _sender = sender;
        _dedupe = dedupe;
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

            // Basic retry with jittered backoff: 3 attempts ~0.5s, 2s, 8s (+/- 20%)
            var baseDelaysMs = new[] { 500, 2000, 8000 };
            var attempt = 0;
            Exception? last = null;

            var scopeState = new Dictionary<string, object?>
            {
                ["email.kind"] = msg.Kind.ToString(),
                // Redact recipient address in logs/scopes (Notif-25)
                ["email.to"] = EmailRedactor.Redact(msg.ToEmail)
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

            // Optional dedupe suppression
            if (!string.IsNullOrWhiteSpace(msg.DedupeKey) && _dedupe is not null)
            {
                // 10 minutes TTL by default
                if (!_dedupe.TryMark(msg.DedupeKey!, TimeSpan.FromMinutes(10)))
                {
                    _logger.LogInformation("Email suppressed by dedupe (key={Key})", msg.DedupeKey);
                    continue;
                }
            }

            var rand = Random.Shared;
            for (; attempt < baseDelaysMs.Length; attempt++)
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
                    if (attempt < baseDelaysMs.Length - 1)
                    {
                        try
                        {
                            // apply +/-20% jitter
                            var ms = baseDelaysMs[attempt];
                            var jitter = (int)(ms * 0.2);
                            var wait = ms + rand.Next(-jitter, jitter + 1);
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
                EmailMetrics.RecordFailed(msg.Kind.ToString());
                if (!string.IsNullOrWhiteSpace(msg.DedupeKey))
                {
                    _logger.LogError(last, "Email permanently failed after {Attempts} attempts (dedupeKey={Key})", attempt, msg.DedupeKey);
                }
                else
                {
                    _logger.LogError(last, "Email permanently failed after {Attempts} attempts", attempt);
                }
            }
        }

        _logger.LogInformation("EmailDispatcher stopping");
    }
}
