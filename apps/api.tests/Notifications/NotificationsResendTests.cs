using System;
using System.Linq;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.App.Notifications;

namespace Appostolic.Api.Tests.Notifications;

public class NotificationsResendTests
{
    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"resend-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Can_insert_child_with_resend_of_fk()
    {
        using var db = NewDb();
        var parent = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Verification,
            ToEmail = "user@example.com",
            DataJson = "{}",
            Status = NotificationStatus.Queued
        };
        db.Notifications.Add(parent);
        db.SaveChanges();

        var child = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Verification,
            ToEmail = "user@example.com",
            DataJson = "{}",
            Status = NotificationStatus.Queued,
            ResendOfNotificationId = parent.Id,
            ResendReason = "user_requested"
        };
        db.Notifications.Add(child);
        db.SaveChanges();

        var got = db.Notifications.Single(n => n.Id == child.Id);
        got.ResendOfNotificationId.Should().Be(parent.Id);
        got.ResendReason.Should().Be("user_requested");
        got.ResendCount.Should().Be(0);
    }
}
