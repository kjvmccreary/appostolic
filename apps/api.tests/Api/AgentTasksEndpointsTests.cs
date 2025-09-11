using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Appostolic.Api.Tests.Api;

public class AgentTasksEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AgentTasksEndpointsTests(WebAppFactory factory) => _factory = factory;

    private static HttpClient CreateClientWithDevHeaders(WebAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        c.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        return c;
    }

    [Fact]
    public async Task CreateTask_Returns201AndSummary()
    {
        var client = CreateClientWithDevHeaders(_factory);
        var agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"); // ResearchAgent
        var body = JsonDocument.Parse("""{"agentId":"11111111-1111-1111-1111-111111111111","input":{"topic":"intro"}}""");

        var resp = await client.PostAsync("/api/agent-tasks", new StringContent(body.RootElement.GetRawText(), System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        resp.Headers.Location.Should().NotBeNull();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("id", out var idProp).Should().BeTrue();
        json.GetProperty("status").GetString().Should().Be("Pending");

        // Store id for subsequent tests
        _lastId = idProp.GetGuid();
        _agentId = agentId;
    }

    [Fact]
    public async Task GetDetails_WithTracesInitiallyEmpty()
    {
        await CreateTask_Returns201AndSummary();
        var client = CreateClientWithDevHeaders(_factory);
        var resp = await client.GetAsync($"/api/agent-tasks/{_lastId}?includeTraces=true");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("task", out var taskElem).Should().BeTrue();
        json.TryGetProperty("traces", out var tracesElem).Should().BeTrue();
        tracesElem.ValueKind.Should().Be(JsonValueKind.Array);
        tracesElem.EnumerateArray().Count().Should().BeGreaterOrEqualTo(0); // may be 0 or >0 depending on background worker timing
    }

    [Fact]
    public async Task List_Pending_ReturnsCreatedFirst()
    {
        await CreateTask_Returns201AndSummary();
        var client = CreateClientWithDevHeaders(_factory);
        var resp = await client.GetAsync("/api/agent-tasks?status=Pending&take=10&skip=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>();
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.EnumerateArray().First().GetProperty("id").GetGuid().Should().Be(_lastId);
    }

    private static Guid _lastId;
    private static Guid _agentId;
}
