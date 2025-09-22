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

public class AgentTasksE2E_List : IClassFixture<WebApplicationFactory<Program>>
{
    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    var dbName = $"e2e-list-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));
                });
            });

    [Fact(Timeout = 10000)] // complete within 10s
    public async Task Inbox_List_Orders_By_CreatedAt_Desc_And_Pages()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SeedPasswordAsync(factory, "kevin@example.com", DefaultPw);
        await AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "kevin@example.com", "kevin-personal");

        // Helper to create a task
        async Task<Guid> CreateTaskAsync(string topic)
        {
            var body = JsonSerializer.Serialize(new
            {
                agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                input = new { topic }
            });
            var res = await client.PostAsync("/api/agent-tasks", new StringContent(body, Encoding.UTF8, "application/json"));
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("id").GetGuid();
        }

        // Create 3 tasks in sequence
        var t1 = await CreateTaskAsync("one");
        var t2 = await CreateTaskAsync("two");
        var t3 = await CreateTaskAsync("three");

        // Optionally wait briefly to ensure CreatedAt ordering is distinct
        await Task.Delay(50);

        // Poll all to terminal quickly to avoid flakiness
        async Task WaitTerminal(Guid id)
        {
            for (var i = 0; i < 40; i++)
            {
                var r = await client.GetAsync($"/api/agent-tasks/{id}");
                if (r.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await Task.Delay(100);
                    continue;
                }
                r.EnsureSuccessStatusCode();
                using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = d.RootElement.GetProperty("status").GetString();
                if (s is "Succeeded" or "Failed" or "Canceled") return;
                await Task.Delay(100);
            }
        }

        await Task.WhenAll(WaitTerminal(t1), WaitTerminal(t2), WaitTerminal(t3));

        // GET page 1: take=2, skip=0 => expect t3, t2 (most recent first)
        var list1 = await client.GetAsync("/api/agent-tasks?take=2&skip=0");
        list1.EnsureSuccessStatusCode();
        using var listDoc1 = JsonDocument.Parse(await list1.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, listDoc1.RootElement.ValueKind);
        Assert.Equal(2, listDoc1.RootElement.GetArrayLength());
        var id0 = listDoc1.RootElement[0].GetProperty("id").GetGuid();
        var id1 = listDoc1.RootElement[1].GetProperty("id").GetGuid();
        Assert.Equal(t3, id0);
        Assert.Equal(t2, id1);

        // GET page 2: take=2, skip=2 => expect t1 only
        var list2 = await client.GetAsync("/api/agent-tasks?take=2&skip=2");
        list2.EnsureSuccessStatusCode();
        using var listDoc2 = JsonDocument.Parse(await list2.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, listDoc2.RootElement.ValueKind);
        Assert.Equal(1, listDoc2.RootElement.GetArrayLength());
        var onlyId = listDoc2.RootElement[0].GetProperty("id").GetGuid();
        Assert.Equal(t1, onlyId);
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
            // Create user with password fields already populated to avoid duplicate tracking update.
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
            return; // no further update required
        }

        // Existing user path: attach updated password fields via new immutable record instance
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
