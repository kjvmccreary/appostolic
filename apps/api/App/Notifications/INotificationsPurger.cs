using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Notifications;

public interface INotificationsPurger
{
    Task<PurgeResult> PurgeOnceAsync(AppDbContext db, DateTimeOffset now, NotificationOptions options, CancellationToken ct = default);
}

public sealed record PurgeResult(int DedupesPurged, int SentPurged, int FailedPurged, int DeadPurged)
{
    public int TotalPurged => DedupesPurged + SentPurged + FailedPurged + DeadPurged;
}

public sealed class NotificationsPurger : INotificationsPurger
{
    public async Task<PurgeResult> PurgeOnceAsync(AppDbContext db, DateTimeOffset now, NotificationOptions options, CancellationToken ct = default)
    {
        int dedupesPurged = 0, sentPurged = 0, failedPurged = 0, deadPurged = 0;

        var relational = db.Database.IsRelational();

        // Dedupe keys
        if (relational)
        {
            dedupesPurged = await db.NotificationDedupes
                .Where(d => d.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct);
        }
        else
        {
            var expired = await db.NotificationDedupes.Where(d => d.ExpiresAt <= now).ToListAsync(ct);
            db.NotificationDedupes.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            dedupesPurged = expired.Count;
        }

        // Notifications
        var sentCutoff = now - options.RetainSentFor;
        var failedCutoff = now - options.RetainFailedFor;
        var deadCutoff = now - options.RetainDeadLetterFor;

        if (relational)
        {
            sentPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Sent && n.SentAt != null && n.SentAt <= sentCutoff)
                .ExecuteDeleteAsync(ct);

            failedPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Failed && n.UpdatedAt <= failedCutoff)
                .ExecuteDeleteAsync(ct);

            deadPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.DeadLetter && n.UpdatedAt <= deadCutoff)
                .ExecuteDeleteAsync(ct);
        }
        else
        {
            var toRemoveSent = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Sent && n.SentAt != null && n.SentAt <= sentCutoff)
                .ToListAsync(ct);
            sentPurged = toRemoveSent.Count;
            db.Notifications.RemoveRange(toRemoveSent);

            var toRemoveFailed = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Failed && n.UpdatedAt <= failedCutoff)
                .ToListAsync(ct);
            failedPurged = toRemoveFailed.Count;
            db.Notifications.RemoveRange(toRemoveFailed);

            var toRemoveDead = await db.Notifications
                .Where(n => n.Status == NotificationStatus.DeadLetter && n.UpdatedAt <= deadCutoff)
                .ToListAsync(ct);
            deadPurged = toRemoveDead.Count;
            db.Notifications.RemoveRange(toRemoveDead);

            await db.SaveChangesAsync(ct);
        }

        return new PurgeResult(dedupesPurged, sentPurged, failedPurged, deadPurged);
    }
}
