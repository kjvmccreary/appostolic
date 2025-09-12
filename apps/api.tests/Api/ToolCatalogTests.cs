using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.Api;

public class ToolCatalogTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ToolCatalogTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient CreateClientWithDevHeaders(WebAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        c.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task Tools_Catalog_Lists_Registered_Tools_With_Categories()
    {
        var client = CreateClientWithDevHeaders(_factory);

        var resp = await client.GetAsync("/api/agents/tools");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        payload.ValueKind.Should().Be(JsonValueKind.Array);

        var names = payload.EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
        names.Should().BeEquivalentTo(new[] { "db.query", "fs.write", "web.search" });

        var cat = payload.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("category").GetString());
        cat["web.search"].Should().Be("search");
        cat["db.query"].Should().Be("db");
        cat["fs.write"].Should().Be("fs");

        // Basic description presence
        foreach (var e in payload.EnumerateArray())
        {
            e.TryGetProperty("description", out var desc).Should().BeTrue();
            desc.ValueKind.Should().Be(JsonValueKind.String);
            desc.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
