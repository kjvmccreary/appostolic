using System.Net;
using System.Net.Http;
using System.Threading;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests;

public class SendGridEmailSenderTests
{
    [Fact]
    public async Task Sends_on_202_Accepted()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var factory = new SingleClientFactory(new HttpClient(handler));
        var sender = new SendGridEmailSender(factory, Options.Create(new SendGridOptions { ApiKey = "SG.abc" }), Options.Create(new EmailOptions()), NullLogger<SendGridEmailSender>.Instance);

        await sender.SendAsync("x@example.com", "subj", "<b>hi</b>", "hi");
    }

    [Fact]
    public async Task Throws_on_400()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("oops") });
        var factory = new SingleClientFactory(new HttpClient(handler));
        var sender = new SendGridEmailSender(factory, Options.Create(new SendGridOptions { ApiKey = "SG.abc" }), Options.Create(new EmailOptions()), NullLogger<SendGridEmailSender>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("x@example.com", "subj", "<b>hi</b>", "hi"));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }
}
