namespace Appostolic.Api.App.Options;

public sealed class SendGridOptions
{
    public string ApiKey { get; set; } = string.Empty;
    // Optional shared secret/token to protect the webhook endpoint.
    // When set, requests must include header X-SendGrid-Token with this exact value.
    public string? WebhookToken { get; set; }
}
