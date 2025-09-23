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
using Appostolic.Api.AuthTests; // TestAuthSeeder for deterministic token issuance

namespace Appostolic.Api.Tests.E2E;

public class AgentTasksE2E_HappyPath : IClassFixture<WebApplicationFactory<Program>>
{
    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    // Stable in-memory db across all DbContext instances (named for isolation)
                    var dbName = $"e2e-happy-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));
                });
            });

    [Fact(Timeout = 10000)] // complete within 10s
    public async Task EndToEnd_CreatesTask_RunsToSuccess_TracesAndTokensPresent()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Deterministic token issuance replaces password seeding + login/select flow.
    var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(factory, "kevin@example.com", "kevin-personal", owner: true);
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Create task for ResearchAgent (seeded)
        var createBody = JsonSerializer.Serialize(new
        {
            agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            input = new { topic = "Beatitudes" }
        });
        var createRes = await client.PostAsync("/api/agent-tasks", new StringContent(createBody, Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        var createdJson = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, taskId);

        // Poll for completion
        var done = false;
        JsonDocument? finalDoc = null;
        for (var i = 0; i < 50 && !done; i++) // 50 * 200ms = 10s max (test has 10s timeout too)
        {
            var getRes = await client.GetAsync($"/api/agent-tasks/{taskId}?includeTraces=true");
            if (getRes.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await Task.Delay(200);
                continue;
            }
            getRes.EnsureSuccessStatusCode();
            finalDoc?.Dispose();
            finalDoc = JsonDocument.Parse(await getRes.Content.ReadAsStringAsync());
            var status = finalDoc.RootElement.GetProperty("task").GetProperty("status").GetString();
            if (status is "Succeeded" or "Failed" or "Canceled")
            {
                done = true;
                break;
            }
            await Task.Delay(200);
        }

        Assert.True(done, "Task did not reach a terminal status within the polling window.");
        Assert.NotNull(finalDoc);

        using (finalDoc!)
        {
            var taskEl = finalDoc!.RootElement.GetProperty("task");
            var tracesEl = finalDoc!.RootElement.GetProperty("traces");

            var status = taskEl.GetProperty("status").GetString();
            Assert.Equal("Succeeded", status);

            // traces.Count >= 2 and ordered by stepNumber ascending
            Assert.True(tracesEl.ValueKind == JsonValueKind.Array);
            Assert.True(tracesEl.GetArrayLength() >= 2);
            var stepNumbers = tracesEl.EnumerateArray().Select(t => t.GetProperty("stepNumber").GetInt32()).ToArray();
            var sorted = stepNumbers.OrderBy(x => x).ToArray();
            Assert.Equal(sorted, stepNumbers);

            // Contains Model and Tool kinds
            var kinds = tracesEl.EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToArray();
            Assert.Contains("Model", kinds);
            Assert.Contains("Tool", kinds);

            // Tokens/cost
            var totalTokens = taskEl.TryGetProperty("totalTokens", out var tt) ? tt.GetInt32() : 0;
            Assert.True(totalTokens > 0);
            if (taskEl.TryGetProperty("estimatedCostUsd", out var costEl) && costEl.ValueKind != JsonValueKind.Null)
            {
                Assert.True(costEl.GetDecimal() > 0);
            }
        }
    }
    // Legacy flow auth helper removed—token issued directly above.
}
