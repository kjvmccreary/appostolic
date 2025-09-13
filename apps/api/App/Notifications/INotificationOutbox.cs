using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Notifications;

public interface INotificationOutbox
{
    Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class EfNotificationOutbox : INotificationOutbox
{
    private readonly AppDbContext _db;

    public EfNotificationOutbox(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default)
    {
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
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
