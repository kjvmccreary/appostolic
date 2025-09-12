namespace Appostolic.Api.App.Notifications;

public interface ITemplateRenderer
{
    Task<(string Subject, string Html, string Text)> RenderAsync(EmailMessage msg, CancellationToken ct);
}
