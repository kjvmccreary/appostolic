using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Notifications;

public interface INotificationsPurger
{
    Task<PurgeResult> PurgeOnceAsync(AppDbContext db, DateTimeOffset now, NotificationOptions options, CancellationToken ct = default);
}

public sealed record PurgeResult(int DedupesPurged, int SentPurged, int FailedPurged, int DeadPurged, int PiiScrubbed)
{
    public int TotalPurged => DedupesPurged + SentPurged + FailedPurged + DeadPurged;
}

public sealed class NotificationsPurger : INotificationsPurger
{
    public async Task<PurgeResult> PurgeOnceAsync(AppDbContext db, DateTimeOffset now, NotificationOptions options, CancellationToken ct = default)
    {
    int dedupesPurged = 0, sentPurged = 0, failedPurged = 0, deadPurged = 0, piiScrubbed = 0;

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
    var sentScrubCutoff = now - options.ScrubSentAfter;
    var failedScrubCutoff = now - options.ScrubFailedAfter;
    var deadScrubCutoff = now - options.ScrubDeadLetterAfter;

        if (relational)
        {
            // Purge
            sentPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Sent && n.SentAt != null && n.SentAt <= sentCutoff)
                .ExecuteDeleteAsync(ct);

            failedPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.Failed && n.UpdatedAt <= failedCutoff)
                .ExecuteDeleteAsync(ct);

            deadPurged = await db.Notifications
                .Where(n => n.Status == NotificationStatus.DeadLetter && n.UpdatedAt <= deadCutoff)
                .ExecuteDeleteAsync(ct);

            // PII scrubbing (before delete windows), skip rows already deleted
            if (options.PiiScrubEnabled)
            {
                // Build update projection that nulls or empties selected fields
                var toScrubSent = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.Sent && n.SentAt != null && n.SentAt <= sentScrubCutoff && n.SentAt > sentCutoff)
                    .Select(n => n.Id)
                    .ToListAsync(ct);
                var toScrubFailed = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.Failed && n.UpdatedAt <= failedScrubCutoff && n.UpdatedAt > failedCutoff)
                    .Select(n => n.Id)
                    .ToListAsync(ct);
                var toScrubDead = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.DeadLetter && n.UpdatedAt <= deadScrubCutoff && n.UpdatedAt > deadCutoff)
                    .Select(n => n.Id)
                    .ToListAsync(ct);

                piiScrubbed += await ScrubByIdsAsync(db, toScrubSent, options, ct);
                piiScrubbed += await ScrubByIdsAsync(db, toScrubFailed, options, ct);
                piiScrubbed += await ScrubByIdsAsync(db, toScrubDead, options, ct);
            }
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

            // PII scrubbing (in-memory path)
            if (options.PiiScrubEnabled)
            {
                var toScrubSent = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.Sent && n.SentAt != null && n.SentAt <= sentScrubCutoff && n.SentAt > sentCutoff)
                    .ToListAsync(ct);
                var toScrubFailed = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.Failed && n.UpdatedAt <= failedScrubCutoff && n.UpdatedAt > failedCutoff)
                    .ToListAsync(ct);
                var toScrubDead = await db.Notifications
                    .Where(n => n.Status == NotificationStatus.DeadLetter && n.UpdatedAt <= deadScrubCutoff && n.UpdatedAt > deadCutoff)
                    .ToListAsync(ct);

                piiScrubbed += ScrubEntities(toScrubSent, options);
                piiScrubbed += ScrubEntities(toScrubFailed, options);
                piiScrubbed += ScrubEntities(toScrubDead, options);
            }

            await db.SaveChangesAsync(ct);
        }

        return new PurgeResult(dedupesPurged, sentPurged, failedPurged, deadPurged, piiScrubbed);
    }

    private static int ScrubEntities(List<Notification> list, NotificationOptions options)
    {
        int count = 0;
        foreach (var n in list)
        {
            if (ApplyScrub(n, options)) count++;
        }
        return count;
    }

    private static async Task<int> ScrubByIdsAsync(AppDbContext db, List<Guid> ids, NotificationOptions options, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var affected = 0;
        // Update in manageable batches to avoid parameter limits
        const int batchSize = 200;
        for (int i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var rows = await db.Notifications
                .Where(n => batch.Contains(n.Id))
                .ToListAsync(ct);
            affected += ScrubEntities(rows, options);
        }
        await db.SaveChangesAsync(ct);
        return affected;
    }

    private static bool ApplyScrub(Notification n, NotificationOptions options)
    {
        bool changed = false;
        if (options.ScrubToName && !string.IsNullOrEmpty(n.ToName)) { n.ToName = null; changed = true; }
        if (options.ScrubSubject && !string.IsNullOrEmpty(n.Subject)) { n.Subject = null; changed = true; }
        if (options.ScrubBodyHtml && !string.IsNullOrEmpty(n.BodyHtml)) { n.BodyHtml = null; changed = true; }
        if (options.ScrubBodyText && !string.IsNullOrEmpty(n.BodyText)) { n.BodyText = null; changed = true; }
        if (options.ScrubToEmail && !string.IsNullOrEmpty(n.ToEmail)) { n.ToEmail = "redacted@example.com"; changed = true; }
        if (changed) n.UpdatedAt = DateTimeOffset.UtcNow;
        return changed;
    }
}
