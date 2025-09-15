using Appostolic.Api.App.Options;

namespace Appostolic.Api.App.Notifications;

public interface INotificationEnqueuer
{
    Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default);
    Task QueueInviteAsync(string toEmail, string? toName, string tenant, string role, string inviter, string token, CancellationToken ct = default);
    Task QueueMagicLinkAsync(string toEmail, string? toName, string token, CancellationToken ct = default);
    Task QueuePasswordResetAsync(string toEmail, string? toName, string token, CancellationToken ct = default);
}

public sealed class NotificationEnqueuer : INotificationEnqueuer
{
    private readonly INotificationOutbox _outbox;
    private readonly INotificationTransport _transport;
    private readonly EmailOptions _emailOptions;

    public NotificationEnqueuer(INotificationOutbox outbox, INotificationTransport transport, Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions)
    {
        _outbox = outbox;
        _transport = transport;
        _emailOptions = emailOptions.Value;
    }

    public async Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var tokenHash = HashToken(token);
        var baseUrl = _emailOptions.WebBaseUrl?.TrimEnd('/') ?? "";
        var link = string.IsNullOrEmpty(baseUrl)
            ? $"/auth/verify?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/auth/verify?token={Uri.EscapeDataString(token)}";

        var data = new Dictionary<string, object?>
        {
            ["link"] = link
        };

        var dedupeKey = $"verification::{NormalizeEmail(toEmail)}::{tokenHash}";
    var msg = new EmailMessage(EmailKind.Verification, toEmail, toName, data, dedupeKey);
    // Pre-render to avoid storing raw token in Data beyond link
    var renderer = new ScribanTemplateRenderer(Microsoft.Extensions.Options.Options.Create(_emailOptions));
    var snapshots = await renderer.RenderAsync(msg, ct);
    var id = await _outbox.CreateQueuedAsync(msg, tokenHash, snapshots, ct);
        await _transport.PublishQueuedAsync(id, ct);
    }

    public async Task QueueInviteAsync(string toEmail, string? toName, string tenant, string role, string inviter, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("tenant is required", nameof(tenant));
        if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException("role is required", nameof(role));
        if (string.IsNullOrWhiteSpace(inviter)) throw new ArgumentException("inviter is required", nameof(inviter));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var tokenHash = HashToken(token);
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

        var dedupeKey = $"invite::{NormalizeEmail(toEmail)}::{tenant}::{role}::{tokenHash}";
    var msg = new EmailMessage(EmailKind.Invite, toEmail, toName, data, dedupeKey);
    var renderer = new ScribanTemplateRenderer(Microsoft.Extensions.Options.Options.Create(_emailOptions));
    var snapshots = await renderer.RenderAsync(msg, ct);
    var id = await _outbox.CreateQueuedAsync(msg, tokenHash, snapshots, ct);
        await _transport.PublishQueuedAsync(id, ct);
    }

    public async Task QueueMagicLinkAsync(string toEmail, string? toName, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var tokenHash = HashToken(token);
        var baseUrl = _emailOptions.WebBaseUrl?.TrimEnd('/') ?? "";
        var link = string.IsNullOrEmpty(baseUrl)
            ? $"/magic/verify?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/magic/verify?token={Uri.EscapeDataString(token)}";

        var data = new Dictionary<string, object?> { ["link"] = link };
        var dedupeKey = $"magiclink::{NormalizeEmail(toEmail)}::{tokenHash}";

        var msg = new EmailMessage(EmailKind.MagicLink, toEmail, toName, data, dedupeKey);
        var renderer = new ScribanTemplateRenderer(Microsoft.Extensions.Options.Options.Create(_emailOptions));
        var snapshots = await renderer.RenderAsync(msg, ct);
        var id = await _outbox.CreateQueuedAsync(msg, tokenHash, snapshots, ct);
        await _transport.PublishQueuedAsync(id, ct);
    }

    public async Task QueuePasswordResetAsync(string toEmail, string? toName, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new ArgumentException("toEmail is required", nameof(toEmail));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token is required", nameof(token));

        var tokenHash = HashToken(token);
        var baseUrl = _emailOptions.WebBaseUrl?.TrimEnd('/') ?? "";
        var link = string.IsNullOrEmpty(baseUrl)
            ? $"/reset-password?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}";

        var data = new Dictionary<string, object?> { ["link"] = link };
        var dedupeKey = $"pwreset::{NormalizeEmail(toEmail)}::{tokenHash}";

        var msg = new EmailMessage(EmailKind.PasswordReset, toEmail, toName, data, dedupeKey);
        var renderer = new ScribanTemplateRenderer(Microsoft.Extensions.Options.Options.Create(_emailOptions));
        var snapshots = await renderer.RenderAsync(msg, ct);
        var id = await _outbox.CreateQueuedAsync(msg, tokenHash, snapshots, ct);
        await _transport.PublishQueuedAsync(id, ct);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string HashToken(string token)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
