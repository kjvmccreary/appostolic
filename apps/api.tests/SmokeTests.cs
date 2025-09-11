using System.Net;
using FluentAssertions;

namespace Appostolic.Api.Tests;

public class SmokeTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SmokeTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
