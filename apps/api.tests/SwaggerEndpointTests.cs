using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests;

public class SwaggerEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SwaggerEndpointTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SwaggerJson_ReturnsOpenApiDocument()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/swagger/v1/swagger.json");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("\"openapi\"");
        body.Should().Contain("Appostolic API");
    }

    [Fact]
    public async Task SwaggerUI_ReturnsHtml()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/swagger/");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        contentType.Should().Be("text/html");
        var html = await resp.Content.ReadAsStringAsync();
        html.Should().Contain("Appostolic API");
        html.Should().Contain("swagger-ui"); // core UI container
    }
}
