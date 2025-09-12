using Appostolic.Api.App.Options;

namespace Appostolic.Api.App.Notifications;

public interface INotificationEnqueuer
{
    Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default);
}

public sealed class NotificationEnqueuer : INotificationEnqueuer
{
    private readonly IEmailQueue _queue;
    private readonly EmailOptions _emailOptions;

    public NotificationEnqueuer(IEmailQueue queue, Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions)
    {
        _queue = queue;
        _emailOptions = emailOptions.Value;
    }

    public Task QueueVerificationAsync(string toEmail, string? toName, string token, CancellationToken ct = default)
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

        var msg = new EmailMessage(EmailKind.Verification, toEmail, toName, data);
        return _queue.EnqueueAsync(msg, ct).AsTask();
    }
}
