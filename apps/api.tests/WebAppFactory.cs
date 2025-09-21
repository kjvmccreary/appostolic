using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;

namespace Appostolic.Api.Tests;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"testdb-{Guid.NewGuid()}";
    private readonly Dictionary<string,string?> _overrides = new();

    /// <summary>
    /// Clone-like helper that returns the same factory instance with additional in-memory configuration overrides
    /// applied during ConfigureAppConfiguration. Tests can call .WithSettings(new{"KEY"="value"}) before CreateClient().
    /// </summary>
    public WebAppFactory WithSettings(Dictionary<string,string?> settings)
    {
        foreach (var kvp in settings)
        {
            _overrides[kvp.Key] = kvp.Value;
        }
        return this;
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Inject test helper configuration flags BEFORE services run so endpoint mapping sees them
        builder.ConfigureAppConfiguration((ctx, cfg) =>
        {
            var dict = new Dictionary<string, string?>
            {
                // Enable JWT test helper endpoints (Story 2a)
                ["AUTH__TEST_HELPERS_ENABLED"] = "true",
                // Ensure JWT itself is enabled for tests (defensive)
                ["AUTH__JWT__ENABLED"] = "true",
                // Story 4: enable refresh cookie issuance in tests
                ["AUTH__REFRESH_COOKIE_ENABLED"] = "true"
            };
            // Apply overrides from tests (Story 8 flag scenarios, etc.)
            foreach (var kvp in _overrides)
            {
                dict[kvp.Key] = kvp.Value;
            }
            cfg.AddInMemoryCollection(dict!);
        });

        // Add early middleware to allow tests to simulate HTTPS by sending X-Test-HTTPS: 1 header.
        // Implemented as a dedicated middleware class to avoid signature mismatch with Use(Func<RequestDelegate,RequestDelegate>).
        // Register startup filter that injects HTTPS simulation middleware without replacing the Program.cs pipeline.
        builder.ConfigureServices(svcs =>
        {
            svcs.AddTransient<IStartupFilter, TestHttpsStartupFilter>();
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing AppDbContext registrations so we can swap to InMemory
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) || d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var d in dbDescriptors) services.Remove(d);

            // Use unique in-memory DB per factory instance
            services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(_dbName));

            // Remove background AgentTaskWorker to avoid flakiness
            var hostedToRemove = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null && d.ImplementationType.Name.Contains("AgentTaskWorker")).ToList();
            foreach (var d in hostedToRemove) services.Remove(d);

            // Remove notification hosted services for deterministic tests
            var notifHosted = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null && (
                d.ImplementationType.Name.Contains("EmailDispatcherHostedService") ||
                d.ImplementationType.Name.Contains("NotificationDispatcherHostedService") ||
                d.ImplementationType.Name.Contains("NotificationsPurgeHostedService")
            )).ToList();
            foreach (var d in notifHosted) services.Remove(d);

            // Disable dispatcher hosted services
            services.PostConfigure<NotificationsRuntimeOptions>(o =>
            {
                o.RunDispatcher = false;
                o.RunLegacyEmailDispatcher = false;
            });

            // Ensure notification outbox/enqueuer use scoped lifetime
            var outboxDesc = services.SingleOrDefault(d => d.ServiceType == typeof(INotificationOutbox));
            if (outboxDesc != null) services.Remove(outboxDesc);
            services.AddScoped<INotificationOutbox, EfNotificationOutbox>();

            var enqDesc = services.SingleOrDefault(d => d.ServiceType == typeof(INotificationEnqueuer));
            if (enqDesc != null) services.Remove(enqDesc);
            services.AddScoped<INotificationEnqueuer, NotificationEnqueuer>();

            // Seed dev user/tenant/membership expected by DevHeaderAuthHandler (flags-only model)
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            if (!db.Users.AsNoTracking().Any(u => u.Email == "kevin@example.com"))
            {
                var tenant = new Tenant { Id = Guid.NewGuid(), Name = "kevin-personal", CreatedAt = DateTime.UtcNow };
                var user = new User { Id = Guid.NewGuid(), Email = "kevin@example.com", CreatedAt = DateTime.UtcNow };
                var membership = new Membership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    // Full composite flags for initial admin user
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

/// <summary>
/// Startup filter injecting a middleware that converts requests with header X-Test-HTTPS:1 into HTTPS (sets Request.Scheme).
/// Ensures it runs early while preserving the rest of the application's pipeline defined in Program.cs.
/// </summary>
internal sealed class TestHttpsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nxt) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-HTTPS", out var vals) && vals.ToString() == "1")
                {
                    context.Request.Scheme = "https";
                    if (!context.Request.Headers.ContainsKey("X-Forwarded-Proto"))
                        context.Request.Headers["X-Forwarded-Proto"] = "https";
                }
                await nxt();
            });
            next(app);
        };
    }
}
/// <summary>
/// Test-only middleware that forces Request.Scheme = https when header X-Test-HTTPS: 1 is present.
/// Enables Secure cookie verification in integration tests without standing up TLS.
/// </summary>
// (Old dedicated middleware class removed; inline middleware above keeps file concise for tests.)
