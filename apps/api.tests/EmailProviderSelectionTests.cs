using Appostolic.Api.App.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests;

public class EmailProviderSelectionTests
{
    [Fact]
    public void Resolves_Smtp_sender_when_provider_smtp()
    {
        var factory = new WebAppFactory()
            .WithWebHostBuilder(_ => { });

        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var sp = factory.Services;
        var sender = sp.GetRequiredService<IEmailSender>();
        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void Resolves_SendGrid_sender_when_provider_sendgrid()
    {
        var factory = new WebAppFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Email:Provider", "sendgrid");
                builder.UseSetting("SendGrid:ApiKey", "SG.test-key");
            });

        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var sp = factory.Services;
        var sender = sp.GetRequiredService<IEmailSender>();
        Assert.IsType<SendGridEmailSender>(sender);
    }
}
