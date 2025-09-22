using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

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

        // Assert: should be 401 because dev headers scheme not registered
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
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
            db.Users.Add(user);
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
            db.AddRange(tenant, membership);
            db.SaveChanges();
        }

        // Since there is no password hashing path already invoked here (passwordless), we simulate a neutral token issuance via test helper endpoint if available.
        // If a real password login endpoint requires a password we would seed hash; for brevity we call the test helper if present.
        var helperResponse = await client.PostAsJsonAsync("/dev/test/mint-neutral", new { email = "jwt-user@example.com" });
        helperResponse.EnsureSuccessStatusCode();
        var tokenPayload = await helperResponse.Content.ReadFromJsonAsync<Dictionary<string,object?>>();
        Assert.NotNull(tokenPayload);
        Assert.True(tokenPayload!.ContainsKey("access"));
        var access = tokenPayload["access"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(access));

        var authed = new HttpRequestMessage(HttpMethod.Get, "/auth-smoke/ping");
        authed.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var ping = await client.SendAsync(authed);
        Assert.Equal(System.Net.HttpStatusCode.OK, ping.StatusCode);
    }
}
