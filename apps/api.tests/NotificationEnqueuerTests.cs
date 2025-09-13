using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests;

public class NotificationEnqueuerTests
{
    [Fact]
    public async Task QueueVerification_enqueues_correct_message()
    {
        // Arrange
        var capturingOutbox = new CapturingOutbox();
        var capturingIds = new CapturingIdQueue();
        var services = new ServiceCollection();
        services.AddSingleton<INotificationOutbox>(capturingOutbox);
        services.AddSingleton<INotificationIdQueue>(capturingIds);
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        // Act
        await enqueuer.QueueVerificationAsync("user@example.com", "User", "tok123");

        // Assert
        var msg = Assert.Single(capturingOutbox.Captured);
        Assert.Equal(EmailKind.Verification, msg.Kind);
        Assert.Equal("user@example.com", msg.ToEmail);
        Assert.Equal("User", msg.ToName);
        Assert.True(msg.Data.TryGetValue("link", out var linkObj) && linkObj is string);
        var link = (string)linkObj!;
        Assert.Equal("http://localhost:3000/auth/verify?token=tok123", link);
        Assert.NotEqual(Guid.Empty, capturingIds.LastId);
    }

    [Fact]
    public async Task QueueVerification_uses_relative_when_base_missing()
    {
        var capturingOutbox = new CapturingOutbox();
        var capturingIds = new CapturingIdQueue();
        var services = new ServiceCollection();
        services.AddSingleton<INotificationOutbox>(capturingOutbox);
        services.AddSingleton<INotificationIdQueue>(capturingIds);
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueVerificationAsync("user@example.com", null, "tok 123");

        var msg = Assert.Single(capturingOutbox.Captured);
        var link = (string)msg.Data["link"]!;
        Assert.Equal("/auth/verify?token=tok%20123", link); // token URL-encoded
    }

    private sealed class CapturingOutbox : INotificationOutbox
    {
        public List<EmailMessage> Captured { get; } = new();
        public Task<Guid> CreateQueuedAsync(EmailMessage message, CancellationToken ct = default)
        {
            Captured.Add(message);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<Guid> CreateQueuedAsync(EmailMessage message, string? tokenHash, (string Subject, string Html, string Text)? snapshots, CancellationToken ct = default)
        {
            Captured.Add(message);
            return Task.FromResult(Guid.NewGuid());
        }

        // Unused in these tests
        public Task<Appostolic.Api.Domain.Notifications.Notification?> LeaseNextDueAsync(CancellationToken ct = default) => Task.FromResult<Appostolic.Api.Domain.Notifications.Notification?>(null);
        public Task MarkDeadLetterAsync(Guid id, string error, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkFailedAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkSentAsync(Guid id, string subject, string html, string? text, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TryRequeueAsync(Guid id, CancellationToken ct = default) => Task.FromResult(false);
        public Task UpdateProviderStatusAsync(Guid id, string provider, string status, DateTimeOffset eventAt, string? reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingIdQueue : INotificationIdQueue
    {
        private readonly System.Threading.Channels.Channel<Guid> _channel = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        public Guid LastId { get; private set; }
        public System.Threading.Channels.ChannelReader<Guid> Reader => _channel.Reader;
        public async ValueTask EnqueueAsync(Guid notificationId, CancellationToken ct = default)
        {
            LastId = notificationId;
            await _channel.Writer.WriteAsync(notificationId, ct);
        }
    }

    [Fact]
    public async Task QueueInvite_enqueues_correct_message()
    {
    var capturingOutbox = new CapturingOutbox();
    var capturingIds = new CapturingIdQueue();
        var services = new ServiceCollection();
    services.AddSingleton<INotificationOutbox>(capturingOutbox);
    services.AddSingleton<INotificationIdQueue>(capturingIds);
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueInviteAsync("invitee@example.com", null, "Acme", "Admin", "Alice", "tok-789");

        var msg = Assert.Single(capturingOutbox.Captured);
        Assert.Equal(EmailKind.Invite, msg.Kind);
        Assert.Equal("invitee@example.com", msg.ToEmail);
        Assert.Null(msg.ToName);
        Assert.Equal("Acme", msg.Data["tenant"]);
        Assert.Equal("Admin", msg.Data["role"]);
        Assert.Equal("Alice", msg.Data["inviter"]);
        var link = (string)msg.Data["link"]!;
    Assert.Equal("http://localhost:3000/invite/accept?token=tok-789", link);
    }

    [Fact]
    public async Task QueueInvite_uses_relative_when_base_missing()
    {
        var capturingOutbox = new CapturingOutbox();
        var capturingIds = new CapturingIdQueue();
        var services = new ServiceCollection();
        services.AddSingleton<INotificationOutbox>(capturingOutbox);
        services.AddSingleton<INotificationIdQueue>(capturingIds);
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueInviteAsync("invitee@example.com", "Buddy", "Team", "Viewer", "Bob", "tok 123");

        var msg = Assert.Single(capturingOutbox.Captured);
        var link = (string)msg.Data["link"]!;
    Assert.Equal("/invite/accept?token=tok%20123", link);
    }
}
