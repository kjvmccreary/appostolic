using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.AuthTests; // Auth helpers (legacy + deterministic seeder)

namespace Appostolic.Api.Tests.AgentTasks;

public class AgentTasksFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"agenttasks-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            // Swap DB to in-memory to isolate tests and keep background worker enabled
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor != null)
            {
                services.Remove(dbDescriptor);
            }
            services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(_dbName));

            // Seed database (but DO NOT issue tokens here – different transient key material may exist pre-host build)
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();

            var email = "dev@example.com";
            var slug = "acme";
            if (!db.Users.AsNoTracking().Any(u => u.Email == email))
            {
                var tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = DateTime.UtcNow };
                var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
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
        });
    }
}

public abstract class AgentTasksTestBase : IClassFixture<AgentTasksFactory>
{
    protected readonly AgentTasksFactory Factory;
    protected readonly HttpClient Client;

    protected static readonly Guid ResearchAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid FilesAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected AgentTasksTestBase(AgentTasksFactory factory)
    {
        Factory = factory;
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Deterministic auth: directly mint a tenant token (avoids fragile multi-step login/select flow that
        // now intermittently returns 400 in tests after membership/refresh refactors). We intentionally
        // keep seeding logic inside the factory (tenant/user/membership) then rely on TestAuthSeeder to
        // issue a scoped token using the production IJwtTokenService so claims & signing keys match.
    var issued = TestAuthSeeder.IssueTenantTokenAsync(Factory, "dev@example.com", "acme", owner: true);
        var token = issued.GetAwaiter().GetResult().token;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected async Task<Guid> CreateTaskAsync(Guid agentId, object input, int? enqueueDelayMs = null, bool suppressEnqueue = false)
    {
        var json = JsonSerializer.Serialize(new { agentId, input });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/agent-tasks");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (enqueueDelayMs.HasValue && enqueueDelayMs.Value > 0)
        {
            req.Headers.Add("x-test-enqueue-delay-ms", enqueueDelayMs.Value.ToString());
        }
        if (suppressEnqueue)
        {
            req.Headers.Add("x-test-suppress-enqueue", "true");
        }
        var resp = await Client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    protected async Task<(string Status, JsonDocument? Result, DateTime CreatedAt)> GetTaskAsync(Guid id, bool includeTraces = false)
    {
        var resp = await Client.GetAsync($"/api/agent-tasks/{id}?includeTraces={(includeTraces ? "true" : "false")}");
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("task", out var taskObj))
        {
            var status = taskObj.GetProperty("status").GetString()!;
            var createdAt = taskObj.GetProperty("createdAt").GetDateTime();
            JsonDocument? result = null;
            if (taskObj.TryGetProperty("result", out var resultEl) && resultEl.ValueKind != JsonValueKind.Null)
            {
                result = JsonDocument.Parse(resultEl.GetRawText());
            }
            return (status, result, createdAt);
        }
        else
        {
            var status = doc.RootElement.GetProperty("status").GetString()!;
            var createdAt = doc.RootElement.GetProperty("createdAt").GetDateTime();
            JsonDocument? result = null;
            if (doc.RootElement.TryGetProperty("result", out var resultEl) && resultEl.ValueKind != JsonValueKind.Null)
            {
                result = JsonDocument.Parse(resultEl.GetRawText());
            }
            return (status, result, createdAt);
        }
    }

    protected async Task WaitUntilAsync(Guid id, Func<string, bool> isTerminal, TimeSpan timeout, TimeSpan? poll = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var delay = poll ?? TimeSpan.FromMilliseconds(150);
        while (sw.Elapsed < timeout)
        {
            var (status, _, _) = await GetTaskAsync(id, includeTraces: false);
            if (isTerminal(status)) return;
            await Task.Delay(delay);
        }
        throw new TimeoutException($"Task {id} did not reach terminal condition within {timeout}.");
    }

    protected async Task ClearAllTasksAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RemoveRange(db.Set<AgentTrace>());
        db.RemoveRange(db.Set<AgentTask>());
        await db.SaveChangesAsync();
    }

    // Legacy password seeding & flow-based auth removed; retained comment for historical context.
}
