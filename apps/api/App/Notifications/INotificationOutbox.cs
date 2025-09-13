using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Notifications;

public interface INotificationOutbox
{
    Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default);

    // Lease the next due notification (Status=Queued and due by NextAttemptAt) and transition it to Sending atomically if possible.
    // Returns null when nothing is due.
    Task<Notification?> LeaseNextDueAsync(CancellationToken ct = default);

    // Mark results of an attempt; these increment AttemptCount and update timestamps.
    Task MarkSentAsync(Guid id, string subject, string html, string? text, CancellationToken ct = default);
    Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken ct = default);
    Task MarkDeadLetterAsync(Guid id, string error, CancellationToken ct = default);

    // Move a Failed or DeadLetter notification back to Queued for retry. Returns false if not found.
    Task<bool> TryRequeueAsync(Guid id, CancellationToken ct = default);

    // Store provider delivery status details on an existing notification.
    // This is additive and idempotent-friendly (last write wins). Data is recorded under data_json provider_status fields.
    Task UpdateProviderStatusAsync(Guid id, string provider, string status, DateTimeOffset eventAt, string? reason, CancellationToken ct = default);
}

public sealed class EfNotificationOutbox : INotificationOutbox
{
    private readonly AppDbContext _db;
    private readonly Appostolic.Api.App.Options.NotificationOptions _options;

    public EfNotificationOutbox(AppDbContext db, Microsoft.Extensions.Options.IOptions<Appostolic.Api.App.Options.NotificationOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        // First, claim dedupe key if provided (TTL window)
        if (!string.IsNullOrWhiteSpace(message.DedupeKey))
        {
            var dd = new Appostolic.Api.Domain.Notifications.NotificationDedupe
            {
                DedupeKey = message.DedupeKey!,
                ExpiresAt = now.Add(_options.DedupeTtl),
                CreatedAt = now,
                UpdatedAt = now
            };
            // Pre-check to avoid EF.InMemory tracking exception on Add
            var exists = await _db.NotificationDedupes.AnyAsync(x => x.DedupeKey == dd.DedupeKey && x.ExpiresAt > now, ct);
            if (exists)
            {
                throw new DuplicateNotificationException("Duplicate dedupe key within TTL");
            }
            _db.NotificationDedupes.Add(dd);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Assume PK conflict → duplicate within TTL
                throw new DuplicateNotificationException("Duplicate dedupe key within TTL", ex);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be tracked because another instance with the same key value", StringComparison.OrdinalIgnoreCase))
            {
                // InMemory provider throws tracking conflict instead of DbUpdateException
                throw new DuplicateNotificationException("Duplicate dedupe key within TTL", ex);
            }
        }

        var entity = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = message.Kind,
            ToEmail = message.ToEmail,
            ToName = message.ToName,
            DataJson = System.Text.Json.JsonSerializer.Serialize(message.Data ?? new Dictionary<string, object?>()),
            DedupeKey = message.DedupeKey,
            Status = NotificationStatus.Queued,
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Notifications.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDedupeUniqueViolation(ex))
        {
            throw new DuplicateNotificationException("Duplicate active notification", ex);
        }

        return entity.Id;
    }

    public async Task<Notification?> LeaseNextDueAsync(CancellationToken ct = default)
    {
        // Simple provider-agnostic approach: find earliest due Queued and flip to Sending within a transaction.
        // In Npgsql we could use SKIP LOCKED for high concurrency; keeping it simple for now.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var item = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.Queued && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .OrderBy(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (item is null)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        item.Status = NotificationStatus.Sending;
        item.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return item;
    }

    public async Task MarkSentAsync(Guid id, string subject, string html, string? text, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException($"Notification {id} not found");
        n.Subject = subject;
        n.BodyHtml = html;
        n.BodyText = text;
        n.Status = NotificationStatus.Sent;
        n.AttemptCount++;
        n.SentAt = now;
        n.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException($"Notification {id} not found");
        n.LastError = error;
        n.Status = NotificationStatus.Failed; // transient; will be picked up when due
        n.AttemptCount++;
        n.NextAttemptAt = nextAttemptAt;
        n.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkDeadLetterAsync(Guid id, string error, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException($"Notification {id} not found");
        n.LastError = error;
        n.Status = NotificationStatus.DeadLetter;
        n.AttemptCount++;
        n.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryRequeueAsync(Guid id, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return false;
        if (n.Status != NotificationStatus.Failed && n.Status != NotificationStatus.DeadLetter)
        {
            return false;
        }
        n.Status = NotificationStatus.Queued;
        n.NextAttemptAt = null;
        n.LastError = null;
        n.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UpdateProviderStatusAsync(Guid id, string provider, string status, DateTimeOffset eventAt, string? reason, CancellationToken ct = default)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException($"Notification {id} not found");
        // Merge provider status under a nested object in DataJson
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(n.DataJson) ?? new();
        var key = "provider_status";
        var payload = new Dictionary<string, object?>
        {
            ["provider"] = provider,
            ["status"] = status,
            ["event_at"] = eventAt,
            ["reason"] = reason
        };
        dict[key] = payload;
        n.DataJson = System.Text.Json.JsonSerializer.Serialize(dict);
        n.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsDedupeUniqueViolation(DbUpdateException ex)
    {
        // Check inner exception or message for our partial unique index name
        return ex.InnerException?.Message.Contains("ux_notifications_dedupe_key_active", StringComparison.OrdinalIgnoreCase) == true
            || ex.Message.Contains("ux_notifications_dedupe_key_active", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DuplicateNotificationException : Exception
{
    public DuplicateNotificationException(string message, Exception? inner = null) : base(message, inner) { }
}
