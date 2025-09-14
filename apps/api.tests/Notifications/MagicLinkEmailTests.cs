using System.Text.Json;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Notifications;

public class MagicLinkEmailTests
{
    [Fact]
    public async Task Renders_MagicLink_Snapshots_With_Link()
    {
        var opts = Options.Create(new EmailOptions { WebBaseUrl = "http://localhost:3000" });
        var renderer = new ScribanTemplateRenderer(opts);

        var msg = new EmailMessage(
            EmailKind.MagicLink,
            ToEmail: "user@example.com",
            ToName: "User",
            Data: new Dictionary<string, object?> { ["link"] = "http://localhost:3000/magic/verify?token=abc" }
        );

        var (subject, html, text) = await renderer.RenderAsync(msg, default);
        Assert.Contains("Sign in", subject);
        Assert.Contains("/magic/verify?token=abc", html);
        Assert.Contains("/magic/verify?token=abc", text);
    }
}
