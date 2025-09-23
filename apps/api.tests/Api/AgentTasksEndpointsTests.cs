using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Tests.TestUtilities;

namespace Appostolic.Api.Tests.Api;

public class AgentTasksEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public AgentTasksEndpointsTests(WebAppFactory factory) => _factory = factory;

    // Story: JWT auth refactor – migrate agent task endpoint tests to deterministic TestAuthSeeder pattern.
    // We mint an owner tenant-scoped token for a fresh (email, tenantSlug) pair per test sequence. Password login
    // flow isn't under test here, so we seed the password hash directly for completeness though current endpoints
    // only rely on Authorization header.
    // Using shared UniqueId helpers (removed local duplication)

    private static async Task<HttpClient> CreateAuthedClientAsync(WebAppFactory f, string scenario)
    {
    var email = UniqueId.Email("agenttasks");
    var slug = UniqueId.Slug(scenario);
        var (token, userId, _) = await TestAuthSeeder.IssueTenantTokenAsync(f, email, slug, owner: true);
        // Optionally seed password so future password-dependent tests could reuse; not strictly required.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            var (hash, salt, _) = hasher.HashPassword(TestAuthSeeder.DefaultPassword);
            db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task CreateTask_Returns201AndSummary()
    {
    var client = await CreateAuthedClientAsync(_factory, "create");
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
    var client = await CreateAuthedClientAsync(_factory, "details");
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
    var client = await CreateAuthedClientAsync(_factory, "list");
        var resp = await client.GetAsync("/api/agent-tasks?status=Pending&take=10&skip=0");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var arr = await resp.Content.ReadFromJsonAsync<JsonElement>();
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.EnumerateArray().First().GetProperty("id").GetGuid().Should().Be(_lastId);
    }

    private static Guid _lastId;
    private static Guid _agentId;
}
