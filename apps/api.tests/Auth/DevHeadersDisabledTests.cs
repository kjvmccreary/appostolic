using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Negative-path coverage asserting that dev headers are rejected when the feature flag is disabled.
/// This file intentionally still references x-dev-user / x-tenant to validate rejection behavior.
/// It is EXCLUDED from Phase B completion criteria and will migrate / be removed in Story 3 (Deprecation Mode).
/// </summary>
public class DevHeadersDisabledTests
{
    [Fact]
    public async Task DevHeaders_Request_Fails_When_Flag_Disabled()
    {
        // Arrange: clone factory with dev headers disabled
        var factory = new WebAppFactory()
            .WithSettings(new Dictionary<string,string?>
            {
                ["AUTH__ALLOW_DEV_HEADERS"] = "false"
            });
        var client = factory.CreateClient();

        // Act: attempt to call an endpoint that requires auth using dev headers only
        var req = new HttpRequestMessage(HttpMethod.Get, "/auth-smoke/ping");
        req.Headers.Add("x-dev-user", "kevin@example.com");
        req.Headers.Add("x-tenant", "kevin-personal");
        var resp = await client.SendAsync(req);

    // Assert: 401 with structured deprecation code
    Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
    var body = await resp.Content.ReadFromJsonAsync<Dictionary<string,string>>();
    Assert.Equal("dev_headers_deprecated", body?["code"]);
    }

    [Fact]
    public async Task JwtLogin_Flow_Works_When_DevHeaders_Disabled()
    {
        // Arrange
        var factory = new WebAppFactory()
            .WithSettings(new Dictionary<string,string?>
            {
                ["AUTH__ALLOW_DEV_HEADERS"] = "false"
            });
        var client = factory.CreateClient();

        // We need a seeded user with password; for simplicity create one directly via the DbContext
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User { Id = Guid.NewGuid(), Email = "jwt-user@example.com", CreatedAt = DateTime.UtcNow };
            // Minimal membership so login selects tenant
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = "jwt-tenant", CreatedAt = DateTime.UtcNow };
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            db.AddRange(user, tenant, membership);
            // Add password hash for real login flow
            var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
            var (hash, salt, _) = hasher.HashPassword("Password123!");
            var updated = user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Entry(user).CurrentValues.SetValues(updated);
            db.SaveChanges();
        }
        // Act: perform real login + select-tenant flow (ensures password verification and refresh rotation path alive)
        var (_, tenantAccess, _) = await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "jwt-user@example.com", "jwt-tenant");
        var ping = await client.GetAsync("/auth-smoke/ping");
        Assert.Equal(System.Net.HttpStatusCode.OK, ping.StatusCode);
    }
}
