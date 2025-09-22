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
using Appostolic.Api.AuthTests;

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
                    var dbName = $"e2e-allowlist-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));
                });
            });

    [Fact(Timeout = 10000)] // complete within 10s
    public async Task Allowlist_Denied_Tool_Produces_Failed_Task_With_Error_Trace()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SeedPasswordAsync(factory, "kevin@example.com", DefaultPw);
        await AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "kevin@example.com", "kevin-personal");

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
    private const string DefaultPw = "Password123!";
    private static async Task SeedPasswordAsync(WebApplicationFactory<Program> factory, string email, string pw)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var (hash, salt, _) = hasher.HashPassword(pw);
        if (user == null)
        {
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = "kevin-personal", CreatedAt = DateTime.UtcNow };
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                CreatedAt = DateTime.UtcNow,
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordUpdatedAt = DateTime.UtcNow
            };
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
            await db.SaveChangesAsync();
            return;
        }
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
