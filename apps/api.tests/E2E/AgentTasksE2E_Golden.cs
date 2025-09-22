using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Xunit;
using Appostolic.Api.AuthTests;

namespace Appostolic.Api.Tests.E2E;

public class AgentTasksE2E_Golden
{
    private static WebApplicationFactory<Program> CreateFactory()
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    var dbName = $"e2e-golden-{Guid.NewGuid()}";
                    var dbRoot = new InMemoryDatabaseRoot();
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName, dbRoot));
                });
            });

    [Fact(Timeout = 30000)]
    public async Task HappyPath_projection_matches_golden_fixture()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SeedPasswordAsync(factory, "kevin@example.com", DefaultPw);
        await AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "kevin@example.com", "kevin-personal");

        // Create task for seeded ResearchAgent
        var create = await client.PostAsJsonAsync("/api/agent-tasks", new
        {
            agentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            input = new { topic = "Beatitudes" }
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<JsonObject>();
        var id = Guid.Parse(created!["id"]!.ToString());

        // Poll until terminal
        JsonObject? details = null;
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(15))
        {
            var res = await client.GetAsync($"/api/agent-tasks/{id}?includeTraces=true");
            if (!res.IsSuccessStatusCode)
            {
                await Task.Delay(200);
                continue;
            }
            details = await res.Content.ReadFromJsonAsync<JsonObject>();
            var status = details!["task"]!["status"]!.ToString();
            if (status is "Succeeded" or "Failed" or "Canceled") break;
            await Task.Delay(200);
        }

        details.Should().NotBeNull("task should be retrievable");

        // Project only stable, non-volatile fields (copy primitives, don't reparent nodes)
        var task = (JsonObject)details!["task"]!;
        var traces = (JsonArray)details!["traces"]!;
        var projTraces = new JsonArray(
            traces!
                .OfType<JsonObject>()
                .Select(t => new JsonObject
                {
                    ["stepNumber"] = JsonValue.Create(int.TryParse(t["stepNumber"]?.ToString(), out var sn) ? sn : 0),
                    ["kind"] = JsonValue.Create(t["kind"]?.ToString() ?? string.Empty),
                    ["name"] = JsonValue.Create(t["name"]?.ToString() ?? string.Empty),
                })
                .ToArray()
        );

        var projection = new JsonObject
        {
            ["status"] = JsonValue.Create(task["status"]?.ToString() ?? string.Empty),
            ["totalTokens"] = JsonValue.Create(0), // enforce only structure; assert > 0 separately
            ["traces"] = projTraces
        };

        // Load golden
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "E2E", "fixtures", "golden-task.json");
        File.Exists(goldenPath).Should().BeTrue($"missing fixture at {goldenPath}");
    var golden = JsonNode.Parse(await File.ReadAllTextAsync(goldenPath))!.AsObject();

        // Compare arrays by sequence (ordering matters for regression)
        projection["status"]!.ToString().Should().Be(golden["status"]!.ToString());
    var projTraceArray = projection["traces"]!.AsArray();
        var goldenTraces = golden["traces"]!.AsArray();
        projTraceArray.Count.Should().BeGreaterOrEqualTo(goldenTraces.Count);
        for (int i = 0; i < goldenTraces.Count; i++)
        {
            var a = (JsonObject)projTraceArray[i]!;
            var b = (JsonObject)goldenTraces[i]!;
            a["stepNumber"]!.ToString().Should().Be(b["stepNumber"]!.ToString());
            a["kind"]!.ToString().Should().Be(b["kind"]!.ToString());
            a["name"]!.ToString().Should().Be(b["name"]!.ToString());
        }

        // Separate invariant: total tokens should be > 0 when present
        if (task.TryGetPropertyValue("totalTokens", out var tt) && tt is not null)
        {
            int.TryParse(tt.ToString(), out var n).Should().BeTrue();
            n.Should().BeGreaterThan(0);
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
