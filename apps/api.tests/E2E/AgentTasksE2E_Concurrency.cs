using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Linq;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

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
                    // Stable in-memory db across all DbContext instances
                    var dbName = $"e2e-concurrency-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    // Swap DB to InMemory for isolated, fast E2E runs
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null)
                    {
                        services.Remove(dbDescriptor);
                    }
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));

                    // Override auth to a test scheme that always authenticates
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                });
            });

    [Fact(Timeout = 20000)] // 20s global timeout
    public async Task Worker_Processes_Many_Tasks_Reliably()
    {
        using var factory = CreateFactory();

        // Seed dev user/tenant/membership expected by DevHeaderAuthHandler
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            if (!db.Users.AsNoTracking().Any(u => u.Email == "kevin@example.com"))
            {
                var tenant = new Tenant { Id = TestAuthHandler.TenantId, Name = "kevin-personal", CreatedAt = DateTime.UtcNow };
                var user = new User { Id = TestAuthHandler.UserId, Email = "kevin@example.com", CreatedAt = DateTime.UtcNow };
                var membership = new Membership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                db.AddRange(tenant, user, membership);
                db.SaveChanges();
            }
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Dev headers optional with test auth in place; add anyway for parity
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

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
}
