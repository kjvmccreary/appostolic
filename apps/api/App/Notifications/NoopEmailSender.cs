using Microsoft.Extensions.Logging;

namespace Appostolic.Api.App.Notifications;

public sealed class NoopEmailSender : IEmailSender
{
    private readonly ILogger<NoopEmailSender> _logger;

    public NoopEmailSender(ILogger<NoopEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
    {
        _logger.LogInformation("[NoopEmailSender] Would send to {To}: {Subject}", EmailRedactor.Redact(toEmail), subject);
        return Task.CompletedTask;
    }
}
