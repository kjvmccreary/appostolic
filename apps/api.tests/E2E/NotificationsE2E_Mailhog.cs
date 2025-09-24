using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // AuthTestClientFlow
using Appostolic.Api.Application.Auth; // IPasswordHasher

namespace Appostolic.Api.Tests.E2E;

public class NotificationsE2E_Mailhog
{
    private static async Task<bool> MailhogAvailableAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var res = await http.GetAsync("api/v2/messages", ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Fact(Timeout = 30000)]
    public async Task VerificationEmail_reaches_Mailhog_and_outbox_marks_Sent()
    {
        using var mh = new HttpClient { BaseAddress = new Uri("http://localhost:8025/") };
        if (!await MailhogAvailableAsync(mh, CancellationToken.None))
        {
            // Skip test gracefully when Mailhog is not running locally
            return; // xUnit: returning without asserts marks test as Passed; acceptable as optional E2E
        }

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((ctx, cfg) =>
                {
                    var dict = new Dictionary<string, string?>
                    {
                        ["Email:Provider"] = "smtp",
                        ["Smtp:Host"] = "127.0.0.1",
                        ["Smtp:Port"] = "1025",
                        // Default From helps Mailhog display nicely
                        ["Email:FromAddress"] = "no-reply@appostolic.local",
                        ["Email:FromName"] = "Appostolic",
                        // Enable refresh cookie issuance for auth flow helper (login + select-tenant) since this test uses a fresh WebApplicationFactory
                        ["AUTH__REFRESH_COOKIE_ENABLED"] = "true",
                        // Transitional: also expose plaintext refresh token in JSON for robustness (helper will prefer cookie but keep both during migration)
                        ["AUTH__REFRESH_JSON_EXPOSE_PLAINTEXT"] = "true"
                    };
                    cfg.AddInMemoryCollection(dict);
                });
                builder.ConfigureServices(services =>
                {
                    // Use isolated in-memory DB
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (dbDescriptor != null) services.Remove(dbDescriptor);
                    var dbName = $"notif20-e2e-{Guid.NewGuid()}";
                    services.AddDbContext<AppDbContext>(opts => opts.UseInMemoryDatabase(dbName));

                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.EnsureCreated();

                    // Seed user + tenant with password so we can exercise real login + tenant selection (no dev headers)
                    var email = "dev@example.com";
                    var slug = "acme";
                    var password = "Password123!";
                    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

                    var tenant = db.Tenants.FirstOrDefault(t => t.Name == slug);
                    if (tenant == null)
                    {
                        tenant = new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = DateTime.UtcNow };
                        db.Tenants.Add(tenant);
                    }

                    var user = db.Users.FirstOrDefault(u => u.Email == email);
                    if (user == null)
                    {
                        var (hash, salt, _) = hasher.HashPassword(password);
                        user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow, PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
                        db.Users.Add(user);
                    }
                    else if (user.PasswordHash == null || user.PasswordSalt == null)
                    {
                        var (hash, salt, _) = hasher.HashPassword(password);
                        var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
                        db.Entry(user).CurrentValues.SetValues(updated);
                    }

                    var membership = db.Memberships.FirstOrDefault(m => m.UserId == user.Id && m.TenantId == tenant.Id);
                    if (membership == null)
                    {
                        membership = new Membership
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenant.Id,
                            UserId = user.Id,
                            Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                            Status = MembershipStatus.Active,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Memberships.Add(membership);
                    }

                    db.SaveChanges();
                });
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Exercise real auth flow (login + select tenant) replacing legacy dev headers
    await AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "dev@example.com", "acme");

        var to = "dev@example.com"; // Mailhog will accept any recipient locally
        var token = Guid.NewGuid().ToString("N");

        // Enqueue dev verification email (accepts 202)
        var enqueue = await client.PostAsJsonAsync("/api/dev/notifications/verification", new
        {
            ToEmail = to,
            ToName = "Dev",
            Token = token
        });
        enqueue.EnsureSuccessStatusCode();

        // Poll DB for Sent status
        var started = DateTime.UtcNow;
        Notification? row = null;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(15))
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            row = await db.Notifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).FirstOrDefaultAsync(n => n.ToEmail == to);
            if (row != null && row.Status == NotificationStatus.Sent) break;
            await Task.Delay(200);
        }

        row.Should().NotBeNull("notification row should exist in outbox");
        row!.Status.Should().Be(NotificationStatus.Sent, "dispatcher should send via SMTP/Mailhog in Development");

        // Poll Mailhog API for presence of the email to the recipient
        var seen = false;
        started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(10))
        {
            try
            {
                var payload = await mh.GetFromJsonAsync<MailhogMessages>("api/v2/messages");
                if (payload?.Items is { Count: > 0 })
                {
                    foreach (var m in payload.Items)
                    {
                        // Match on To header or parsed address list
                        var headerTo = m.Content?.Headers != null && m.Content.Headers.TryGetValue("To", out var toHdr)
                            ? string.Join(",", toHdr)
                            : string.Empty;
                        var any = (m.To?.Any() == true && m.To!.Any(a => string.Equals($"{a.Mailbox}@{a.Domain}", to, StringComparison.OrdinalIgnoreCase)))
                                  || (!string.IsNullOrEmpty(headerTo) && headerTo.Contains(to, StringComparison.OrdinalIgnoreCase));
                        if (any)
                        {
                            seen = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // ignore transient errors
            }
            if (seen) break;
            await Task.Delay(300);
        }

        seen.Should().BeTrue("expected to find the email in Mailhog for recipient {0}", to);
    }

    // Minimal Mailhog API response shapes
    public sealed class MailhogMessages
    {
        [JsonPropertyName("items")] public List<MailhogItem>? Items { get; set; }
    }

    public sealed class MailhogItem
    {
        [JsonPropertyName("to")] public List<MailhogAddress>? To { get; set; }
        [JsonPropertyName("content")] public MailhogContent? Content { get; set; }
    }

    public sealed class MailhogAddress
    {
        [JsonPropertyName("Mailbox")] public string Mailbox { get; set; } = string.Empty;
        [JsonPropertyName("Domain")] public string Domain { get; set; } = string.Empty;
    }

    public sealed class MailhogContent
    {
        [JsonPropertyName("Headers")] public Dictionary<string, string[]>? Headers { get; set; }
        [JsonPropertyName("Body")] public string? Body { get; set; }
    }
}
