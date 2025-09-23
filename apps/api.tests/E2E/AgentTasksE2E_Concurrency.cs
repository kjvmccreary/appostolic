using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;
using System.Linq;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.E2E;

public class AgentTasksE2E_Concurrency : IClassFixture<WebApplicationFactory<Program>>
{
    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    var dbName = $"e2e-concurrency-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));
                });
            });

    [Fact(Timeout = 20000)] // 20s global timeout
    public async Task Worker_Processes_Many_Tasks_Reliably()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(factory, "kevin@example.com", "kevin-personal", owner: true);
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create 10 tasks quickly (concurrent posts)
        var createPayloads = Enumerable.Range(1, 10).Select(i => JsonSerializer.Serialize(new
        {
            agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            input = new { topic = $"topic-{i}" }
        })).ToArray();

        async Task<Guid> CreateAsync(string payload)
        {
            var res = await client.PostAsync("/api/agent-tasks", new StringContent(payload, Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("id").GetGuid();
        }

        var createTasks = createPayloads.Select(CreateAsync).ToArray();
        var ids = await Task.WhenAll(createTasks);

        // Poll each task to terminal status (Succeeded/Failed/Canceled) with per-task timeout budget
        async Task<(Guid id, string status, int traceCount)> WaitTerminalAsync(Guid id)
        {
            for (var i = 0; i < 100; i++) // 100 * 100ms = 10s per task; overall Fact has 20s
            {
                var res = await client.GetAsync($"/api/agent-tasks/{id}?includeTraces=true");
                if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await Task.Delay(100);
                    continue;
                }
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var taskEl = doc.RootElement.GetProperty("task");
                var status = taskEl.GetProperty("status").GetString()!;
                if (status is "Succeeded" or "Failed" or "Canceled")
                {
                    var traces = doc.RootElement.GetProperty("traces");
                    var count = traces.ValueKind == JsonValueKind.Array ? traces.GetArrayLength() : 0;
                    return (id, status, count);
                }
                await Task.Delay(100);
            }
            return (id, "Timeout", 0);
        }

        var results = await Task.WhenAll(ids.Select(WaitTerminalAsync));

        // Assertions
        var timeouts = results.Count(r => r.status == "Timeout");
        Assert.True(timeouts == 0, $"Some tasks did not reach terminal state: {string.Join(", ", results.Where(r => r.status == "Timeout").Select(r => r.id))}");

        var succeeded = results.Count(r => r.status == "Succeeded");
        Assert.True(succeeded >= 9, $"Expected >=9 Succeeded, got {succeeded} (statuses: {string.Join(", ", results.Select(r => r.status))})");

        var stuck = results.Count(r => r.status == "Running");
        Assert.True(stuck == 0, "No task should remain Running after timeout window");

        foreach (var r in results)
        {
            Assert.True(r.traceCount >= 2, $"Task {r.id} expected >=2 traces, got {r.traceCount}");
        }
    }
    // Legacy password seeding removed.
}
