using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Api;

public class ToolCatalogTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ToolCatalogTests(WebAppFactory factory) => _factory = factory;

    /// <summary>
    /// Creates an HttpClient with a freshly issued tenant-scoped JWT using the deterministic TestAuthSeeder.
    /// This replaces the prior password + /auth/login + /auth/select-tenant choreography which added noise
    /// for a test whose focus is simply enumerating registered tool metadata.
    /// </summary>
    private static async Task<HttpClient> CreateOwnerClientAsync(WebAppFactory factory)
    {
        var email = "tool-user@example.com";
        var tenantSlug = $"tools-{Guid.NewGuid():N}";
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(factory, email, tenantSlug, owner: true);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Tools_Catalog_Lists_Registered_Tools_With_Categories()
    {
    var client = await CreateOwnerClientAsync(_factory);

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
