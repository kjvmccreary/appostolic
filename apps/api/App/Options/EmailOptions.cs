namespace Appostolic.Api.App.Options;

public sealed class EmailOptions
{
    public string Provider { get; set; } = "smtp"; // smtp | sendgrid
    public string WebBaseUrl { get; set; } = "http://localhost:3000";
    public string FromAddress { get; set; } = "no-reply@appostolic.local";
    public string FromName { get; set; } = "Appostolic";
}
