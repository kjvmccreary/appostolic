using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.Api;

public class AgentsEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AgentsEndpointsTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient CreateClientWithDevHeaders(WebAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        c.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task Agents_CRUD_HappyPath()
    {
        var client = CreateClientWithDevHeaders(_factory);

        // Create
        var name = $"agent-{Guid.NewGuid():N}";
        var create = new
        {
            name,
            systemPrompt = "You are helpful.",
            toolAllowlist = new[] { "web.search" },
            model = "gpt-4o-mini",
            temperature = 0.3,
            maxSteps = 5
        };
        var createResp = await client.PostAsJsonAsync("/api/agents", create);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        createResp.Headers.Location.Should().NotBeNull();
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created!.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be(name);
        created.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        created.GetProperty("temperature").GetDouble().Should().Be(0.3);
        created.GetProperty("maxSteps").GetInt32().Should().Be(5);

        // Get
        var getResp = await client.GetAsync($"/api/agents/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var got = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        got.GetProperty("id").GetGuid().Should().Be(id);
        got.GetProperty("name").GetString().Should().Be(name);

        // List
        var listResp = await client.GetAsync("/api/agents?take=10&skip=0");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        list.ValueKind.Should().Be(JsonValueKind.Array);
        list.EnumerateArray().Any(e => e.GetProperty("id").GetGuid() == id).Should().BeTrue();

        // Duplicate name should 409
        var dupResp = await client.PostAsJsonAsync("/api/agents", create);
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Update
        var newName = name + "-v2";
        var update = new
        {
            name = newName,
            systemPrompt = "Updated.",
            toolAllowlist = new[] { "web.search", "fs.write" },
            model = "gpt-4o-mini",
            temperature = 0.7,
            maxSteps = 7
        };
        var putResp = await client.PutAsJsonAsync($"/api/agents/{id}", update);
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be(newName);
        updated.GetProperty("temperature").GetDouble().Should().Be(0.7);
        updated.GetProperty("maxSteps").GetInt32().Should().Be(7);
        updated.TryGetProperty("updatedAt", out var updatedAt).Should().BeTrue();
        updatedAt.ValueKind.Should().NotBe(JsonValueKind.Null);

        // Delete
        var delResp = await client.DeleteAsync($"/api/agents/{id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfterDel = await client.GetAsync($"/api/agents/{id}");
        getAfterDel.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Agents_Validation_Errors()
    {
        var client = CreateClientWithDevHeaders(_factory);

        // Missing name
        var bad1 = new { name = "", systemPrompt = "", toolAllowlist = Array.Empty<string>(), model = "m", temperature = 0.1, maxSteps = 5 };
        var r1 = await client.PostAsJsonAsync("/api/agents", bad1);
        r1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Temperature out of range
        var bad2 = new { name = "x", systemPrompt = "", toolAllowlist = Array.Empty<string>(), model = "m", temperature = 3.1, maxSteps = 5 };
        var r2 = await client.PostAsJsonAsync("/api/agents", bad2);
        r2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // MaxSteps out of range
        var bad3 = new { name = "y", systemPrompt = "", toolAllowlist = Array.Empty<string>(), model = "m", temperature = 0.1, maxSteps = 0 };
        var r3 = await client.PostAsJsonAsync("/api/agents", bad3);
        r3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
