using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests;

public class NotificationEnqueuerTests
{
    [Fact]
    public async Task QueueVerification_enqueues_correct_message()
    {
        // Arrange
        var captured = new List<EmailMessage>();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailQueue>(new CapturingQueue(captured));
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        // Act
        await enqueuer.QueueVerificationAsync("user@example.com", "User", "tok123");

        // Assert
        Assert.Single(captured);
        var msg = captured[0];
        Assert.Equal(EmailKind.Verification, msg.Kind);
        Assert.Equal("user@example.com", msg.ToEmail);
        Assert.Equal("User", msg.ToName);
        Assert.True(msg.Data.TryGetValue("link", out var linkObj) && linkObj is string);
        var link = (string)linkObj!;
        Assert.Equal("http://localhost:3000/auth/verify?token=tok123", link);
    }

    [Fact]
    public async Task QueueVerification_uses_relative_when_base_missing()
    {
        var captured = new List<EmailMessage>();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailQueue>(new CapturingQueue(captured));
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueVerificationAsync("user@example.com", null, "tok 123");

        var msg = Assert.Single(captured);
        var link = (string)msg.Data["link"]!;
        Assert.Equal("/auth/verify?token=tok%20123", link); // token URL-encoded
    }

    private sealed class CapturingQueue : IEmailQueue
    {
        private readonly List<EmailMessage> _captured;
        public CapturingQueue(List<EmailMessage> captured) => _captured = captured;
        public ChannelReader<EmailMessage> Reader => throw new NotSupportedException();
        public ValueTask EnqueueAsync(EmailMessage message, CancellationToken ct = default)
        {
            _captured.Add(message);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task QueueInvite_enqueues_correct_message()
    {
        var captured = new List<EmailMessage>();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailQueue>(new CapturingQueue(captured));
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "http://localhost:3000");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueInviteAsync("invitee@example.com", null, "Acme", "Admin", "Alice", "tok-789");

        var msg = Assert.Single(captured);
        Assert.Equal(EmailKind.Invite, msg.Kind);
        Assert.Equal("invitee@example.com", msg.ToEmail);
        Assert.Null(msg.ToName);
        Assert.Equal("Acme", msg.Data["tenant"]);
        Assert.Equal("Admin", msg.Data["role"]);
        Assert.Equal("Alice", msg.Data["inviter"]);
        var link = (string)msg.Data["link"]!;
        Assert.Equal("http://localhost:3000/auth/invite/accept?token=tok-789", link);
    }

    [Fact]
    public async Task QueueInvite_uses_relative_when_base_missing()
    {
        var captured = new List<EmailMessage>();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailQueue>(new CapturingQueue(captured));
        services.Configure<EmailOptions>(o => o.WebBaseUrl = "");
        services.AddSingleton<INotificationEnqueuer, NotificationEnqueuer>();

        await using var sp = services.BuildServiceProvider();
        var enqueuer = sp.GetRequiredService<INotificationEnqueuer>();

        await enqueuer.QueueInviteAsync("invitee@example.com", "Buddy", "Team", "Viewer", "Bob", "tok 123");

        var msg = Assert.Single(captured);
        var link = (string)msg.Data["link"]!;
        Assert.Equal("/auth/invite/accept?token=tok%20123", link);
    }
}
