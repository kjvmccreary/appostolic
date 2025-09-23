using System.Net;
using System.Net.Http.Headers;
using Appostolic.Api.Tests.AgentTasks;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.Infrastructure.Auth.Jwt; // IJwtTokenService
using Microsoft.AspNetCore.Mvc.Testing;

namespace Appostolic.Api.Tests.Security;

public class AgentTasksAuthContractTests : IClassFixture<AgentTasksFactory>
{
    private readonly AgentTasksFactory _factory;

    public AgentTasksAuthContractTests(AgentTasksFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListEndpoint_RequiresAuthentication_JwtPath()
    {
        // Arrange
        var unauth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        unauth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Authenticated client: perform real password login + tenant selection to obtain a bearer token.
        var auth = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        auth.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // Deterministic auth seeding (Story 2 migration): directly ensure tenant, user membership and mint a tenant token
        // using the same factory instance to avoid multi-endpoint fragility (login + select-tenant) for non-flow tests.
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = db.Tenants.FirstOrDefault(t => t.Name == "acme");
            if (tenant == null)
            {
                tenant = new Tenant { Id = Guid.NewGuid(), Name = "acme", CreatedAt = DateTime.UtcNow };
                db.Tenants.Add(tenant);
                db.SaveChanges();
            }
            tenantId = tenant.Id;
            var user = db.Users.FirstOrDefault(u => u.Email == "dev@example.com");
            if (user == null)
            {
                user = new User { Id = Guid.NewGuid(), Email = "dev@example.com", CreatedAt = DateTime.UtcNow };
                db.Users.Add(user);
                db.SaveChanges();
            }
            if (!db.Memberships.Any(m => m.UserId == user.Id && m.TenantId == tenantId))
            {
                db.Memberships.Add(new Membership
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TenantId = tenantId,
                    Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
            // Issue a tenant token directly
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var tenantToken = jwt.IssueTenantToken(user.Id.ToString(), tenantId, tenant.Name, (int)(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner), 0, user.Email);
            auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        }

        // Act - unauthenticated
        var resp = await unauth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - unauthenticated is blocked (401 or 403 allowed)
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // Act - authenticated
        var ok = await auth.GetAsync("/api/agent-tasks?take=1&skip=0");

        // Assert - authenticated returns 200 and JSON array (JWT auth)
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await ok.Content.ReadAsStringAsync()).TrimStart();
        body.Should().StartWith("[");

        // Optional sanity: swagger JSON remains publicly readable
        var swagger = await unauth.GetAsync("/swagger/v1/swagger.json");
        swagger.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
