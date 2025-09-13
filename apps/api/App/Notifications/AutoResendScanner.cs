using System.Text.Json;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.App.Notifications;

public interface IAutoResendScanner
{
    Task<int> RunOnceAsync(CancellationToken ct = default);
}

public sealed class AutoResendScanner : IAutoResendScanner
{
    private readonly ILogger<AutoResendScanner> _logger;
    private readonly IServiceProvider _sp;
    private readonly Appostolic.Api.App.Options.NotificationOptions _options;

    public AutoResendScanner(ILogger<AutoResendScanner> logger, IServiceProvider sp, Microsoft.Extensions.Options.IOptions<Appostolic.Api.App.Options.NotificationOptions> options)
    {
        _logger = logger;
        _sp = sp;
        _options = options.Value;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.EnableAutoResend) return 0;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();

        var now = DateTimeOffset.UtcNow;
        var cutoff = now.Subtract(_options.AutoResendNoActionWindow);
        var dailyWindowStart = now.AddDays(-1);

        // Candidate selection:
        // - Original notifications (ResendOfNotificationId IS NULL)
        // - Sent state
        // - CreatedAt older than cutoff (no-action window)
        // - Optional signal: TokenHash present (used by some flows to correlate action); keep broad for now
        // - Not previously auto-resend attempted (enforced by throttle + presence of a child)
        var baseQuery = db.Notifications
            .AsNoTracking()
            .Where(n => n.ResendOfNotificationId == null && n.Status == NotificationStatus.Sent && n.CreatedAt <= cutoff);

        // Order by oldest first; limit per scan
        var candidates = await baseQuery
            .OrderBy(n => n.CreatedAt)
            .Take(_options.AutoResendMaxPerScan)
            .ToListAsync(ct);

        var created = 0;
        // Track per-tenant increments within this scan to avoid race conditions on our own newly created resends
    var perTenantScanAdds = new Dictionary<Guid, int>();
        foreach (var n in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Safety: skip if a child resend already exists (any reason)
            var hasChild = await db.Notifications.AsNoTracking()
                .AnyAsync(x => x.ResendOfNotificationId == n.Id, ct);
            if (hasChild) continue;

            // Optional provider-status filter: if provider explicitly delivered or bounced hard, skip.
            // We store provider_status under DataJson; parse and evaluate best-effort.
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(n.DataJson) ?? new();
                if (dict.TryGetValue("provider_status", out var psObj) && psObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
                {
                    var provider = je.TryGetProperty("provider", out var p) ? p.GetString() : null;
                    var status = je.TryGetProperty("status", out var s) ? s.GetString() : null;
                    // If provider said delivered or opened, treat as action and skip. Otherwise proceed.
                    if (string.Equals(status, "delivered", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "opened", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
            }
            catch
            {
                // ignore malformed provider_status
            }

            // Enforce per-tenant daily cap for automated resends (rolling 24h)
            if (_options.AutoResendPerTenantDailyCap > 0)
            {
                var tenantKey = n.TenantId ?? Guid.Empty; // normalize null to Guid.Empty bucket
                var recentCount = await db.Notifications.AsNoTracking()
                    .CountAsync(x => x.ResendOfNotificationId != null && x.ResendReason == "auto_no_action" && (x.TenantId ?? Guid.Empty) == tenantKey && x.CreatedAt >= dailyWindowStart, ct);
                perTenantScanAdds.TryGetValue(tenantKey, out var added);
                var projected = recentCount + added;
                if (projected >= _options.AutoResendPerTenantDailyCap)
                {
                    EmailMetrics.RecordResend(n.Kind.ToString(), mode: "auto", tenantScope: (n.TenantId.HasValue ? "tenant" : "none"), outcome: "forbidden");
                    _logger.LogDebug("Auto-resend cap reached for tenant {TenantId}; skipping", n.TenantId?.ToString() ?? "(none)");
                    continue;
                }
            }

            // Enforce resend throttle window via outbox
            try
            {
                var id = await outbox.CreateResendAsync(n.Id, reason: "auto_no_action", ct);
                created++;
                EmailMetrics.RecordResend(n.Kind.ToString(), mode: "auto", tenantScope: n.TenantId.HasValue ? "tenant" : "none", outcome: "created");
                // Optionally enqueue for faster pickup
                var idQueue = scope.ServiceProvider.GetRequiredService<INotificationIdQueue>();
                await idQueue.EnqueueAsync(id, ct);
                // Track per-tenant increment
                var key = n.TenantId ?? Guid.Empty;
                perTenantScanAdds[key] = perTenantScanAdds.TryGetValue(key, out var prev) ? prev + 1 : 1;
            }
            catch (ResendThrottledException)
            {
                EmailMetrics.RecordResend(n.Kind.ToString(), mode: "auto", tenantScope: n.TenantId.HasValue ? "tenant" : "none", outcome: "throttled");
                _logger.LogDebug("Auto-resend throttled for {Email}", EmailRedactor.Redact(n.ToEmail));
            }
            catch (Exception ex)
            {
                EmailMetrics.RecordResend(n.Kind.ToString(), mode: "auto", tenantScope: n.TenantId.HasValue ? "tenant" : "none", outcome: "error");
                _logger.LogWarning(ex, "Auto-resend failed to create for notification {Id}", n.Id);
            }
        }

        if (created > 0)
        {
            _logger.LogInformation("AutoResendScanner created {Count} resends", created);
        }
        return created;
    }
}
