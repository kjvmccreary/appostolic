using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class NotificationsPiiScrubTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Scrubs_before_delete_windows_and_deletes_at_cutoff()
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;

        // Options: delete sent after 60d; scrub after 30d
        var opts = Options.Create(new NotificationOptions
        {
            PiiScrubEnabled = true,
            RetainSentFor = TimeSpan.FromDays(60),
            RetainFailedFor = TimeSpan.FromDays(90),
            RetainDeadLetterFor = TimeSpan.FromDays(90),
            ScrubSentAfter = TimeSpan.FromDays(30),
            ScrubFailedAfter = TimeSpan.FromDays(30),
            ScrubDeadLetterAfter = TimeSpan.FromDays(60),
            ScrubSubject = true,
            ScrubBodyHtml = true,
            ScrubBodyText = true,
            ScrubToName = true,
            ScrubToEmail = false
        });

        // Seed: one recent (no scrub), one scrub-eligible, one delete-eligible
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@example.com", ToName = "Alice",
            Subject = "S", BodyHtml = "<b>H</b>", BodyText = "T", Status = NotificationStatus.Sent,
            SentAt = now - TimeSpan.FromDays(10), CreatedAt = now - TimeSpan.FromDays(10), UpdatedAt = now - TimeSpan.FromDays(10)
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "b@example.com", ToName = "Bob",
            Subject = "S", BodyHtml = "<b>H</b>", BodyText = "T", Status = NotificationStatus.Sent,
            SentAt = now - TimeSpan.FromDays(45), CreatedAt = now - TimeSpan.FromDays(45), UpdatedAt = now - TimeSpan.FromDays(45)
        });
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "c@example.com", ToName = "Carol",
            Subject = "S", BodyHtml = "<b>H</b>", BodyText = "T", Status = NotificationStatus.Sent,
            SentAt = now - TimeSpan.FromDays(80), CreatedAt = now - TimeSpan.FromDays(80), UpdatedAt = now - TimeSpan.FromDays(80)
        });
        await db.SaveChangesAsync();

        var purger = new NotificationsPurger();
        var result = await purger.PurgeOnceAsync(db, now, opts.Value);

        result.PiiScrubbed.Should().Be(1);
        result.SentPurged.Should().Be(1);

        var items = await db.Notifications.AsNoTracking().OrderBy(n => n.ToEmail).ToListAsync();
        items.Should().HaveCount(2);

        var recent = items.First(i => i.ToEmail == "a@example.com");
        recent.Subject.Should().Be("S");
        recent.BodyHtml.Should().Be("<b>H</b>");
        recent.BodyText.Should().Be("T");
        recent.ToName.Should().Be("Alice");

        var scrubbed = items.First(i => i.ToEmail == "b@example.com");
        scrubbed.Subject.Should().BeNull();
        scrubbed.BodyHtml.Should().BeNull();
        scrubbed.BodyText.Should().BeNull();
        scrubbed.ToName.Should().BeNull();
    }
}
