using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;

namespace Appostolic.Api.Tests;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"testdb-{Guid.NewGuid()}";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Option 1: keep real Npgsql but isolate by transaction/cleanup (not implemented here).
            // Option 2: swap DB to InMemory for fast/safe tests.
            // Remove any existing AppDbContext registrations (options + context) so we can swap to InMemory
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) || d.ServiceType == typeof(AppDbContext))
                .ToList();
            foreach (var d in dbDescriptors)
            {
                services.Remove(d);
            }

            // Use a unique in-memory database per factory instance to avoid cross-test interference
            services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(_dbName));

            // Remove background AgentTaskWorker to avoid flakiness in tests
            var hostedToRemove = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null && d.ImplementationType.Name.Contains("AgentTaskWorker")).ToList();
            foreach (var d in hostedToRemove)
            {
                services.Remove(d);
            }

            // Also remove notification hosted services to keep tests deterministic
            var notifHosted = services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != null && (
                d.ImplementationType.Name.Contains("EmailDispatcherHostedService") ||
                d.ImplementationType.Name.Contains("NotificationDispatcherHostedService") ||
                d.ImplementationType.Name.Contains("NotificationsPurgeHostedService")
            )).ToList();
            foreach (var d in notifHosted)
            {
                services.Remove(d);
            }

            // Explicitly disable dispatcher hosted services registered via options-driven wrappers
            services.PostConfigure<NotificationsRuntimeOptions>(o =>
            {
                o.RunDispatcher = false;
                o.RunLegacyEmailDispatcher = false;
            });

            // Ensure notification outbox/enqueuer use scoped lifetime (DbContext dependency)
            var outboxDesc = services.SingleOrDefault(d => d.ServiceType == typeof(INotificationOutbox));
            if (outboxDesc != null) services.Remove(outboxDesc);
            services.AddScoped<INotificationOutbox, EfNotificationOutbox>();

            var enqDesc = services.SingleOrDefault(d => d.ServiceType == typeof(INotificationEnqueuer));
            if (enqDesc != null) services.Remove(enqDesc);
            services.AddScoped<INotificationEnqueuer, NotificationEnqueuer>();

            // Seed dev user/tenant/membership expected by DevHeaderAuthHandler
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
                    Role = MembershipRole.Owner,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                };
                db.AddRange(tenant, user, membership);
                db.SaveChanges();
            }
        });
    }
}
