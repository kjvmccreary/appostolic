using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Appostolic.Api.AuthTests; // AuthTestClientFlow

namespace Appostolic.Api.Tests.Api;

public class ToolCatalogTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ToolCatalogTests(WebAppFactory factory) => _factory = factory;

    // RDH Story 2 Phase A: migrate from mint helper to real auth (password login + select tenant)
    private static async Task<HttpClient> CreateAuthedClientAsync(WebAppFactory f)
    {
        var c = f.CreateClient();
        await AuthTestClientFlow.LoginAndSelectTenantAsync(f, c, "kevin@example.com", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task Tools_Catalog_Lists_Registered_Tools_With_Categories()
    {
    var client = await CreateAuthedClientAsync(_factory);

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
