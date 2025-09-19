using System.Security.Claims;
using System.Net.Http;
using System.Net.Http.Json;
using Appostolic.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Security;

public class RoleAuthorizationTests
{
    private static (AppDbContext db, IHttpContextAccessor http, IAuthorizationService auth) MakeServices()
    {
        var services = new ServiceCollection();
        // Add logging so DefaultAuthorizationService can resolve ILogger<DefaultAuthorizationService>
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddHttpContextAccessor();
        services.AddAuthorization(o =>
        {
            o.AddPolicy("Creator", p => p.AddRequirements(new RoleRequirement(Roles.Creator)));
        });
        // Match production: handler consumes scoped services, so register it as Scoped
        services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();

        // Resolve services from a created scope so scoped dependencies work correctly
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        return (sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IHttpContextAccessor>(), sp.GetRequiredService<IAuthorizationService>());
    }

    [Fact]
    public async Task CreatorPolicy_Allows_WhenMembershipHasCreator()
    {
        var (db, http, auth) = MakeServices();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "acme", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { Id = userId, Email = "dev@example.com", CreatedAt = DateTime.UtcNow });
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = MembershipRole.Editor, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ctx = new DefaultHttpContext();
        ctx.Items["TenantId"] = tenantId;
        http.HttpContext = ctx;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
        var result = await auth.AuthorizeAsync(principal, null, "Creator");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CreatorPolicy_Denies_WhenNoMembership()
    {
        var (db, http, auth) = MakeServices();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "acme", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { Id = userId, Email = "dev@example.com", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ctx = new DefaultHttpContext();
        ctx.Items["TenantId"] = tenantId;
        http.HttpContext = ctx;

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId.ToString()) }, "test"));
        var result = await auth.AuthorizeAsync(principal, null, "Creator");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RoleChange_Updates_RolesBitmask_FromLegacyRole()
    {
        // Arrange integration-style using WebAppFactory (in-memory DB already configured there)
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        // Seed: factory already seeds an Owner membership with full flags (15) for kevin@example.com / kevin-personal
        // Create a second user we will demote from Owner->Editor and verify Roles becomes Creator|Learner
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = db.Tenants.Single(t => t.Name == "kevin-personal");
            var user = new User { Id = Guid.NewGuid(), Email = "rolechange@example.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                Role = MembershipRole.Owner,
                Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        // Act: issue PUT to change membership role from Owner -> Editor
        var tenantId = await GetTenantIdAsync(factory, "kevin-personal");
        var userId = await GetUserIdAsync(factory, "rolechange@example.com");

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/tenants/{tenantId}/members/{userId}");
        req.Content = JsonContent.Create(new { role = "Editor" });
        // Dev headers for auth as existing owner (kevin@example.com)
        req.Headers.Add("x-dev-user", "kevin@example.com");
        req.Headers.Add("x-tenant", "kevin-personal");
        var resp = await client.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        // Assert: membership updated with non-zero flags matching legacy Editor mapping (Creator|Learner)
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = db.Memberships.Single(m => m.UserId == userId && m.TenantId == tenantId);
            Assert.Equal(MembershipRole.Editor, membership.Role);
            var expected = Roles.Creator | Roles.Learner;
            Assert.Equal(expected, membership.Roles);
            Assert.NotEqual(Roles.None, membership.Roles);
        }
    }

    private static async Task<Guid> GetTenantIdAsync(WebAppFactory factory, string slug)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var t = await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == slug);
        return t.Id;
    }

    private static async Task<Guid> GetUserIdAsync(WebAppFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var u = await db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        return u.Id;
    }
}
