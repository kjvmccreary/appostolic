using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class NotificationsRetentionTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase($"retention-{Guid.NewGuid()}"));
        services.AddSingleton<INotificationsPurger, NotificationsPurger>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Purger_removes_expired_dedupes_and_old_notifications()
    {
        await using var sp = BuildServices();
        var db = sp.GetRequiredService<AppDbContext>();
        var purger = sp.GetRequiredService<INotificationsPurger>();

        var now = DateTimeOffset.UtcNow;

        // Seed dedupes
        db.NotificationDedupes.AddRange(
            new NotificationDedupe { DedupeKey = "k1", ExpiresAt = now.AddMinutes(-1), CreatedAt = now.AddHours(-2), UpdatedAt = now.AddHours(-2) },
            new NotificationDedupe { DedupeKey = "k2", ExpiresAt = now.AddMinutes(30), CreatedAt = now, UpdatedAt = now }
        );

        // Seed notifications
        db.Notifications.AddRange(
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, SentAt = now.AddDays(-61), CreatedAt = now.AddDays(-61), UpdatedAt = now.AddDays(-61) },
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "b@x.com", DataJson = "{}", Status = NotificationStatus.Sent, SentAt = now.AddDays(-10), CreatedAt = now.AddDays(-10), UpdatedAt = now.AddDays(-10) },
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "c@x.com", DataJson = "{}", Status = NotificationStatus.Failed, CreatedAt = now.AddDays(-100), UpdatedAt = now.AddDays(-100) },
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "d@x.com", DataJson = "{}", Status = NotificationStatus.DeadLetter, CreatedAt = now.AddDays(-200), UpdatedAt = now.AddDays(-200) }
        );
        await db.SaveChangesAsync();

        var options = new NotificationOptions
        {
            RetainSentFor = TimeSpan.FromDays(60),
            RetainFailedFor = TimeSpan.FromDays(90),
            RetainDeadLetterFor = TimeSpan.FromDays(90)
        };

        var result = await purger.PurgeOnceAsync(db, now, options);

        result.DedupesPurged.Should().Be(1);
        result.SentPurged.Should().Be(1);
        result.FailedPurged.Should().Be(1);
        result.DeadPurged.Should().Be(1);

        (await db.NotificationDedupes.CountAsync()).Should().Be(1);
        (await db.Notifications.CountAsync()).Should().Be(1 + 0); // the non-expired Sent remains only
    }
}
