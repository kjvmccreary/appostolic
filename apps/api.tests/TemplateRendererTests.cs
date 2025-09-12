using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using FluentAssertions;
using Microsoft.Extensions.Options;

public class TemplateRendererTests
{
    private static IOptions<EmailOptions> EmailOpts(string webBaseUrl = "http://localhost:3000")
        => Options.Create(new EmailOptions { WebBaseUrl = webBaseUrl });

    [Fact]
    public async Task Verification_template_renders_link_and_subject()
    {
        var r = new ScribanTemplateRenderer(EmailOpts());
        var msg = new EmailMessage(
            EmailKind.Verification,
            ToEmail: "u@example.com",
            ToName: "Test User",
            Data: new Dictionary<string, object?> { ["link"] = "http://localhost:3000/verify?token=abc" }
        );

        var (subject, html, text) = await r.RenderAsync(msg, default);
        subject.Should().Contain("Verify your email");
        html.Should().Contain("verify");
        text.Should().Contain("verify");
    }
}
