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
using Appostolic.Api.Infrastructure.Auth.Jwt; // For IJwtTokenService token issuance

namespace Appostolic.Api.Tests;

public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"testdb-{Guid.NewGuid()}";
    private readonly Dictionary<string,string?> _overrides = new();
    private static readonly object _tokenLock = new();
    private static readonly Dictionary<string,string> _neutralTokens = new();

    /// <summary>
    /// Ensure a user exists for the provided email and return a stable neutral access token (JWT) for that user.
    /// Bypasses the deprecated test mint endpoint so tests (e.g. invite acceptance) can authenticate without
    /// requiring prior tenant membership. Tokens are cached per email for run determinism.
    /// </summary>
    public (string token, Guid userId) EnsureNeutralToken(string email)
    {
        lock (_tokenLock)
        {
            if (_neutralTokens.TryGetValue(email, out var existing))
            {
                // Derive userId from token is non-trivial; re-query DB for user.
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = db.Users.AsNoTracking().First(u => u.Email == email);
                return (existing, user.Id);
            }

            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = db.Users.FirstOrDefault(u => u.Email == email);
                if (user is null)
                {
                    user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
                    db.Users.Add(user);
                    db.SaveChanges();
                }
                var tokenSvc = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
                var token = tokenSvc.IssueNeutralToken(user.Id.ToString(), 0, user.Email);
                _neutralTokens[email] = token;
                return (token, user.Id);
            }
        }
    }

    /// <summary>
    /// Clone-like helper that returns the same factory instance with additional in-memory configuration overrides
    /// applied during ConfigureAppConfiguration. Tests can call .WithSettings(new{"KEY"="value"}) before CreateClient().
    /// </summary>
    public WebAppFactory WithSettings(Dictionary<string,string?> settings)
    {
        // IMPORTANT: Previous implementation mutated a shared _overrides dictionary on the fixture instance.
        // Because xUnit reuses the IClassFixture<WebAppFactory> across all tests in the assembly, overrides
        // from one test (e.g. setting AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT=false) leaked into later tests
        // that expected the default (true) behavior, causing flaky / order-dependent failures.
        // To isolate configuration per test, we return a NEW factory instance that copies existing overrides
        // and applies the new settings, leaving the original fixture state untouched.
        var clone = new WebAppFactory();
        // copy existing overrides first
        foreach (var kvp in _overrides)
        {
            clone._overrides[kvp.Key] = kvp.Value;
        }
        // apply new overrides
        foreach (var kvp in settings)
        {
            clone._overrides[kvp.Key] = kvp.Value;
        }
        return clone;
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
                // Provide a stable symmetric signing key for test runs so tokens remain valid across
                // multiple TestServer instances / HttpClients. Without this, each Program.cs startup
                // (per factory clone) generated a new random key, invalidating previously issued tokens
                // and producing intermittent 401s mid-run. This is a 256-bit (32-byte) base64 value
                // generated once for tests only. DO NOT use in production environments.
                // Provide key under both env-style and options-binding (section) paths for robustness during transition.
                ["AUTH__JWT__SIGNING_KEY"] = "v1KcXbq0cU4o1M3v1d2W9yA4b7F8k2L6p1r8s3u5x7g=", // legacy env pattern some code may read directly
                ["Auth:Jwt:SigningKeyBase64"] = "v1KcXbq0cU4o1M3v1d2W9yA4b7F8k2L6p1r8s3u5x7g=", // bound to AuthJwtOptions.SigningKeyBase64
                // Story 4: enable refresh cookie issuance in tests
                ["AUTH__REFRESH_COOKIE_ENABLED"] = "true",
                // Story 8: Explicitly set plaintext exposure flag true by default so tests relying
                // on default behavior are stable even if developer machine/environment sets it false.
                // Individual tests override to false via WithSettings when validating suppression.
                ["AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT"] = "true"
            };
            // Explicitly enable dev headers in test environment by default; targeted tests can disable by override.
            dict["AUTH__ALLOW_DEV_HEADERS"] = "true";
            // Superadmin allowlist for flow-based elevation (Story 2 Phase A follow-up). This enables
            // login/select-tenant issued tokens for kevin@example.com to include superadmin=true claim
            // eliminating reliance on test mint superadmin helper.
            dict["Auth:SuperAdminEmails"] = "kevin@example.com";
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

    public static Dictionary<string,string> DecodeJwtClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return new Dictionary<string,string>();
        string Base64UrlDecode(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return string.Empty; }
        }
        var payloadJson = Base64UrlDecode(parts[1]);
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
        }
        catch { return new Dictionary<string,string>(); }
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
