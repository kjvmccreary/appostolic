using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Notifications;

public sealed class SendGridEmailSender : IEmailSender
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SendGridOptions> _sgOptions;
    private readonly IOptions<EmailOptions> _emailOptions;
    private readonly ILogger<SendGridEmailSender> _logger;

    private const string ClientName = "sendgrid";

    public SendGridEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptions<SendGridOptions> sgOptions,
        IOptions<EmailOptions> emailOptions,
        ILogger<SendGridEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sgOptions = sgOptions;
        _emailOptions = emailOptions;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken ct = default)
    {
        var apiKey = _sgOptions.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("SendGrid:ApiKey is not configured.");
        }

        var from = new { email = _emailOptions.Value.FromAddress, name = _emailOptions.Value.FromName };
        var to = new { email = toEmail };

        var payload = new
        {
            personalizations = new[] { new { to = new[] { to } } },
            from,
            subject,
            content = BuildContent(htmlBody, textBody)
        };

        var client = _httpClientFactory.CreateClient(ClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = JsonContent.Create(payload, new MediaTypeHeaderValueCompat("application/json"));

        var resp = await client.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.Accepted)
        {
            _logger.LogInformation("SendGrid accepted email to {To}", toEmail);
            return;
        }

        var body = await SafeReadAsync(resp, ct);
        _logger.LogError("SendGrid error {Status}: {Body}", (int)resp.StatusCode, body);
        throw new HttpRequestException($"SendGrid returned {(int)resp.StatusCode}: {body}");
    }

    private static object[] BuildContent(string html, string? text)
    {
        var list = new List<object>();
        if (!string.IsNullOrWhiteSpace(text)) list.Add(new { type = "text/plain", value = text });
        list.Add(new { type = "text/html", value = html });
        return list.ToArray();
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return "<no body>";
        }
    }

    // Minimal shim to construct a MediaTypeHeaderValue compatible with JsonContent.Create without adding a reference here.
    private sealed class MediaTypeHeaderValueCompat : System.Net.Http.Headers.MediaTypeHeaderValue
    {
        public MediaTypeHeaderValueCompat(string mediaType) : base(mediaType) { }
    }
}
