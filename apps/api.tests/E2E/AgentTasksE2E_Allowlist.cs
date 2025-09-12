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

public class AgentTasksE2E_Allowlist : IClassFixture<WebApplicationFactory<Program>>
{
    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    // Stable in-memory db across all DbContext instances
                    var dbName = $"e2e-allowlist-{Guid.NewGuid()}";
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

    [Fact(Timeout = 10000)] // complete within 10s
    public async Task Allowlist_Denied_Tool_Produces_Failed_Task_With_Error_Trace()
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
                    Role = MembershipRole.Owner,
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

        // Create task for ResearchAgent, but instruct model (via input) to plan a disallowed tool: db.query
        var createBody = JsonSerializer.Serialize(new
        {
            agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            input = new
            {
                // MockModelAdapter reads next/tool from context; we encode under input, which our adapter honors
                next = "tool",
                tool = "db.query",
                input = new { table = "users", take = 1 }
            }
        });
        var createRes = await client.PostAsync("/api/agent-tasks", new StringContent(createBody, Encoding.UTF8, "application/json"));
        createRes.EnsureSuccessStatusCode();

        var createdJson = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var taskId = createdJson.RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, taskId);

        // Poll for terminal
        var done = false;
        JsonDocument? finalDoc = null;
        for (var i = 0; i < 50 && !done; i++) // 50 * 200ms = 10s max
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
            Assert.Equal("Failed", status);

            // traces.Count >= 2 (model + tool) and ordered by stepNumber ascending
            Assert.True(tracesEl.ValueKind == JsonValueKind.Array);
            Assert.True(tracesEl.GetArrayLength() >= 2);
            var last = tracesEl.EnumerateArray().Last();
            Assert.Equal("Tool", last.GetProperty("kind").GetString());
            Assert.Equal("db.query", last.GetProperty("name").GetString());

            // Error should be present in output JSON with message containing "Tool not allowed"
            var output = last.GetProperty("output");
            Assert.True(output.ValueKind == JsonValueKind.Object);
            var hasError = output.TryGetProperty("error", out var errEl) && errEl.GetString()!.Contains("Tool not allowed", StringComparison.OrdinalIgnoreCase);
            Assert.True(hasError, $"Expected tool error to contain 'Tool not allowed', got: {output.GetRawText()}");

            // Note: success flag isn't exposed in DTO; error presence implies failure for tool step.
        }
    }
}
