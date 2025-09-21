using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Appostolic.Api.Infrastructure.Providers;

namespace Appostolic.Api.App.Notifications;

public interface INotificationOutbox
{
    Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default);
    Task<Guid> CreateQueuedAsync(EmailMessage message, string? tokenHash, (string Subject, string Html, string Text)? snapshots, CancellationToken ct = default);
    Task<Guid> CreateResendAsync(Guid originalId, string? reason, CancellationToken ct = default);

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
    private readonly IFieldCipher _cipher;

    public EfNotificationOutbox(AppDbContext db, Microsoft.Extensions.Options.IOptions<Appostolic.Api.App.Options.NotificationOptions> options, IFieldCipher cipher)
    {
        _db = db;
        _options = options.Value;
        _cipher = cipher;
    }

    public async Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default)
        => await CreateQueuedAsync(message, tokenHash: null, snapshots: null, ct);

    public async Task<Guid> CreateQueuedAsync(EmailMessage message, string? tokenHash, (string Subject, string Html, string Text)? snapshots, CancellationToken ct = default)
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
            ToName = _options.EncryptFields && _options.EncryptToName && !string.IsNullOrEmpty(message.ToName) ? _cipher.Encrypt(message.ToName!) : message.ToName,
            DataJson = System.Text.Json.JsonSerializer.Serialize(message.Data ?? new Dictionary<string, object?>()),
            DedupeKey = message.DedupeKey,
            TokenHash = tokenHash,
            Status = NotificationStatus.Queued,
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (snapshots is (string subj, string html, string text))
        {
            entity.Subject = _options.EncryptFields && _options.EncryptSubject && !string.IsNullOrEmpty(subj) ? _cipher.Encrypt(subj) : subj;
            entity.BodyHtml = _options.EncryptFields && _options.EncryptBodyHtml && !string.IsNullOrEmpty(html) ? _cipher.Encrypt(html) : html;
            entity.BodyText = _options.EncryptFields && _options.EncryptBodyText && !string.IsNullOrEmpty(text) ? _cipher.Encrypt(text) : text;
        }

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
        // Simple provider-agnostic approach: find earliest due Queued and flip to Sending.
        // Use a transaction for real databases; EF InMemory doesn't support transactions, so skip there.
    var useTx = _db.Database.SupportsExplicitTransactions();
        IDbContextTransaction? tx = null;
        if (useTx)
            tx = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var item = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.Queued && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
            .OrderBy(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (item is null)
        {
            if (tx is not null)
                await tx.CommitAsync(ct);
            return null;
        }

        // Decrypt fields for processing (no-op for NullFieldCipher or if not encrypted)
        if (_options.EncryptFields)
        {
            if (_options.EncryptToName && !string.IsNullOrEmpty(item.ToName)) item.ToName = _cipher.Decrypt(item.ToName!);
            if (_options.EncryptSubject && !string.IsNullOrEmpty(item.Subject)) item.Subject = _cipher.Decrypt(item.Subject!);
            if (_options.EncryptBodyHtml && !string.IsNullOrEmpty(item.BodyHtml)) item.BodyHtml = _cipher.Decrypt(item.BodyHtml!);
            if (_options.EncryptBodyText && !string.IsNullOrEmpty(item.BodyText)) item.BodyText = _cipher.Decrypt(item.BodyText!);
        }

        item.Status = NotificationStatus.Sending;
        item.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return item;
    }

    public async Task<Guid> CreateResendAsync(Guid originalId, string? reason, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
    var useTx = _db.Database.SupportsExplicitTransactions();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (useTx)
            tx = await _db.Database.BeginTransactionAsync(ct);

        var original = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == originalId, ct)
            ?? throw new InvalidOperationException($"Notification {originalId} not found");

        // Disallow resend while still in-flight
        if (original.Status == NotificationStatus.Queued || original.Status == NotificationStatus.Sending)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            throw new InvalidResendStateException($"Cannot resend a notification in status {original.Status}");
        }

        // Throttle by (to_email, kind) based on latest CreatedAt
        var latest = await _db.Notifications
            .Where(n => n.ToEmail == original.ToEmail && n.Kind == original.Kind)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest is not null)
        {
            var nextAllowed = latest.CreatedAt.Add(_options.ResendThrottleWindow);
            if (nextAllowed > now)
            {
                if (tx is not null) await tx.RollbackAsync(ct);
                throw new ResendThrottledException(nextAllowed);
            }
        }

        var clone = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = original.Kind,
            ToEmail = original.ToEmail,
            ToName = original.ToName,
            DataJson = original.DataJson,
            TokenHash = original.TokenHash,
            TenantId = original.TenantId,
            DedupeKey = null,
            ResendOfNotificationId = original.Id,
            ResendReason = reason,
            Status = NotificationStatus.Queued,
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Notifications.Add(clone);

        // Update original metadata
        original.ResendCount += 1;
        original.LastResendAt = now;
        original.ThrottleUntil = now.Add(_options.ResendThrottleWindow);
        original.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return clone.Id;
    }

    public async Task MarkSentAsync(Guid id, string subject, string html, string? text, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException($"Notification {id} not found");
    n.Subject = _options.EncryptFields && _options.EncryptSubject && !string.IsNullOrEmpty(subject) ? _cipher.Encrypt(subject) : subject;
    n.BodyHtml = _options.EncryptFields && _options.EncryptBodyHtml && !string.IsNullOrEmpty(html) ? _cipher.Encrypt(html) : html;
    n.BodyText = _options.EncryptFields && _options.EncryptBodyText && !string.IsNullOrEmpty(text) ? _cipher.Encrypt(text) : text;
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

public sealed class ResendThrottledException : Exception
{
    public DateTimeOffset RetryAfter { get; }
    public ResendThrottledException(DateTimeOffset retryAfter) : base($"Resend throttled until {retryAfter:O}")
    {
        RetryAfter = retryAfter;
    }
}

public sealed class InvalidResendStateException : Exception
{
    public InvalidResendStateException(string message) : base(message) { }
}
