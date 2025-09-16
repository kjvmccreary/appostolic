using Appostolic.Api.Application.Privacy;
using System.Net;
using System.Net.Mail;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Notifications;

public interface ISmtpClientFactory
{
    IAsyncSmtpClient Create();
}

public interface IAsyncSmtpClient : IDisposable
{
    Task SendMailAsync(MailMessage message, CancellationToken cancellationToken = default);
}

internal sealed class DefaultSmtpClientFactory : ISmtpClientFactory
{
    private readonly IOptions<SmtpOptions> _options;
    private readonly ILogger<DefaultSmtpClientFactory> _logger;

    public DefaultSmtpClientFactory(IOptions<SmtpOptions> options, ILogger<DefaultSmtpClientFactory> logger)
    {
        _options = options;
        _logger = logger;
    }

    public IAsyncSmtpClient Create()
    {
        var o = _options.Value;
        var client = new SmtpClient(o.Host, o.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = false,
        };

        if (!string.IsNullOrWhiteSpace(o.User))
        {
            client.Credentials = new NetworkCredential(o.User, o.Pass);
        }

        _logger.LogDebug("SMTP client created host={Host} port={Port} hasAuth={HasAuth}", o.Host, o.Port, !string.IsNullOrWhiteSpace(o.User));
        return new SystemSmtpClientAdapter(client);
    }
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly ISmtpClientFactory _factory;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(ISmtpClientFactory factory, IOptions<EmailOptions> emailOptions, ILogger<SmtpEmailSender> logger)
    {
        _factory = factory;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string html, string? text = null, CancellationToken ct = default)
    {
        using var smtp = _factory.Create();

        var fromAddress = _emailOptions.Value.FromAddress ?? "noreply@example.com";
        var fromName = _emailOptions.Value.FromName ?? "Appostolic";

        using var message = new MailMessage()
        {
            From = new MailAddress(fromAddress, fromName),
            Subject = subject,
            Body = text ?? string.Empty, // set plain text as body, html added as alternate view
            IsBodyHtml = false,
        };

        message.To.Add(new MailAddress(to));

        // Add both text and HTML using alternate views
    var plainView = AlternateView.CreateAlternateViewFromString(text ?? string.Empty, null, "text/plain");
        var htmlView = AlternateView.CreateAlternateViewFromString(html, null, "text/html");
        message.AlternateViews.Add(plainView);
        message.AlternateViews.Add(htmlView);

        try
        {
            _logger.LogInformation("Sending SMTP email to={To} subject={Subject}", PIIRedactor.RedactEmail(to), subject);
            await smtp.SendMailAsync(message, ct);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP send failed to={To} subject={Subject}", PIIRedactor.RedactEmail(to), subject);
            throw;
        }
    }
}

internal sealed class SystemSmtpClientAdapter : IAsyncSmtpClient
{
    private readonly SmtpClient _inner;
    public SystemSmtpClientAdapter(SmtpClient inner) => _inner = inner;
    public void Dispose() => _inner.Dispose();
    public Task SendMailAsync(MailMessage message, CancellationToken cancellationToken = default)
        => _inner.SendMailAsync(message, cancellationToken);
}
