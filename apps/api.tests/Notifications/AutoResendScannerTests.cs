using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Tests.Notifications;

public class AutoResendScannerTests
{
    private sealed class TestOptions : IOptions<Appostolic.Api.App.Options.NotificationOptions>
    {
        public Appostolic.Api.App.Options.NotificationOptions Value { get; }
        public TestOptions(Appostolic.Api.App.Options.NotificationOptions value) { Value = value; }
    }

    private static ServiceProvider BuildProvider(AppDbContext db, Appostolic.Api.App.Options.NotificationOptions opts)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddLogging();
        services.AddSingleton<IOptions<Appostolic.Api.App.Options.NotificationOptions>>(new TestOptions(opts));
        services.AddSingleton<IFieldCipher, NullFieldCipher>();
        services.AddScoped<INotificationOutbox, EfNotificationOutbox>();
        services.AddSingleton<INotificationIdQueue, NotificationIdQueue>();
        services.AddSingleton<IAutoResendScanner, AutoResendScanner>();
        return services.BuildServiceProvider();
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"auto-resend-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Creates_resend_for_old_sent_without_children()
    {
        using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var original = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Verification,
            ToEmail = "user@example.com",
            DataJson = "{}",
            Status = NotificationStatus.Sent,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-2),
            SentAt = now.AddDays(-2)
        };
        db.Notifications.Add(original);
        db.SaveChanges();

        var opts = new Appostolic.Api.App.Options.NotificationOptions
        {
            EnableAutoResend = true,
            AutoResendNoActionWindow = TimeSpan.FromHours(24),
            AutoResendMaxPerScan = 10
        };
        using var sp = BuildProvider(db, opts);
        var scanner = sp.GetRequiredService<IAutoResendScanner>();

        var created = await scanner.RunOnceAsync(CancellationToken.None);
        created.Should().Be(1);
        db.Notifications.Count(n => n.ResendOfNotificationId == original.Id).Should().Be(1);
    }

    [Fact]
    public async Task Skips_when_disabled()
    {
        using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Invite,
            ToEmail = "user2@example.com",
            DataJson = "{}",
            Status = NotificationStatus.Sent,
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddDays(-3),
            SentAt = now.AddDays(-3)
        });
        db.SaveChanges();

        var opts = new Appostolic.Api.App.Options.NotificationOptions
        {
            EnableAutoResend = false,
            AutoResendNoActionWindow = TimeSpan.FromHours(24)
        };
        using var sp = BuildProvider(db, opts);
        var scanner = sp.GetRequiredService<IAutoResendScanner>();
        var created = await scanner.RunOnceAsync(CancellationToken.None);
        created.Should().Be(0);
        db.Notifications.Count(n => n.ResendOfNotificationId != null).Should().Be(0);
    }

    [Fact]
    public async Task Respects_throttle_window()
    {
        using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var original = new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Invite,
            ToEmail = "user3@example.com",
            DataJson = "{}",
            Status = NotificationStatus.Sent,
            CreatedAt = now.AddDays(-3),
            UpdatedAt = now.AddDays(-3),
            SentAt = now.AddDays(-3)
        };
        db.Notifications.Add(original);
        // Add a recent notification to trigger throttle for same (to, kind)
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Invite,
            ToEmail = original.ToEmail,
            DataJson = "{}",
            Status = NotificationStatus.Sent,
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-2),
            SentAt = now.AddMinutes(-2)
        });
        db.SaveChanges();

        var opts = new Appostolic.Api.App.Options.NotificationOptions
        {
            EnableAutoResend = true,
            AutoResendNoActionWindow = TimeSpan.FromHours(24),
            ResendThrottleWindow = TimeSpan.FromMinutes(5)
        };
        using var sp = BuildProvider(db, opts);
        var scanner = sp.GetRequiredService<IAutoResendScanner>();

        var created = await scanner.RunOnceAsync(CancellationToken.None);
        created.Should().Be(0);
        db.Notifications.Count(n => n.ResendOfNotificationId == original.Id).Should().Be(0);
    }
}
