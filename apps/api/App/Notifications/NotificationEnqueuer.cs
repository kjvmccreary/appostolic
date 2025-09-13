using Appostolic.Api.App.Options;

namespace Appostolic.Api.App.Notifications;

public interface INotificationEnqueuer
{
    Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default);
    Task QueueInviteAsync(string toEmail, string? toName, string tenant, string role, string inviter, string token, CancellationToken ct = default);
}

public sealed class NotificationEnqueuer : INotificationEnqueuer
{
    private readonly INotificationOutbox _outbox;
    private readonly INotificationIdQueue _idQueue;
    private readonly EmailOptions _emailOptions;

    public NotificationEnqueuer(INotificationOutbox outbox, INotificationIdQueue idQueue, Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions)
    {
        _outbox = outbox;
        _idQueue = idQueue;
        _emailOptions = emailOptions.Value;
    }

    public async Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var baseUrl = _emailOptions.WebBaseUrl?.TrimEnd('/') ?? "";
        var link = string.IsNullOrEmpty(baseUrl)
            ? $"/auth/verify?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/auth/verify?token={Uri.EscapeDataString(token)}";

        var data = new Dictionary<string, object?>
        {
            ["link"] = link
        };

        var dedupeKey = $"verification::{toEmail}::{token}";
        var msg = new EmailMessage(EmailKind.Verification, toEmail, toName, data, dedupeKey);
        var id = await _outbox.CreateQueuedAsync(msg, ct);
        await _idQueue.EnqueueAsync(id, ct);
    }

    public async Task QueueInviteAsync(string toEmail, string? toName, string tenant, string role, string inviter, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("tenant is required", nameof(tenant));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role is required", nameof(role));
        if (string.IsNullOrWhiteSpace(inviter)) throw new ArgumentException("inviter is required", nameof(inviter));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var baseUrl = _emailOptions.WebBaseUrl?.TrimEnd('/') ?? "";
        var link = string.IsNullOrEmpty(baseUrl)
            ? $"/invite/accept?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/invite/accept?token={Uri.EscapeDataString(token)}";

        var data = new Dictionary<string, object?>
        {
            ["link"] = link,
            ["tenant"] = tenant,
            ["role"] = role,
            ["inviter"] = inviter
        };

        var dedupeKey = $"invite::{toEmail}::{tenant}::{role}::{token}";
        var msg = new EmailMessage(EmailKind.Invite, toEmail, toName, data, dedupeKey);
        var id = await _outbox.CreateQueuedAsync(msg, ct);
        await _idQueue.EnqueueAsync(id, ct);
    }
}
