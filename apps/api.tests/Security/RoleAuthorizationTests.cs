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
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddHttpContextAccessor();
        services.AddAuthorization(o =>
        {
            o.AddPolicy("Creator", p => p.AddRequirements(new RoleRequirement(Roles.Creator)));
        });
        services.AddScoped<IAuthorizationHandler, RoleAuthorizationHandler>();
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
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Roles = Roles.Creator, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
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
    public async Task ApplyRoleChange_Audits_WhenFlagsActuallyChange()
    {
        var (db, http, auth) = MakeServices();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, Name = "acme", CreatedAt = DateTime.UtcNow });
        db.Users.Add(new User { Id = userId, Email = "dev@example.com", CreatedAt = DateTime.UtcNow });
        var membership = new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Roles = Roles.Creator | Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        var newFlags = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
        var audit = membership.ApplyRoleChange(newFlags, changedByEmail: "system@test", changedByUserId: null);
        Assert.NotNull(audit);
        Assert.Equal(Roles.Creator | Roles.Learner, audit!.OldRoles);
        Assert.Equal(newFlags, audit.NewRoles);
    }

    [Fact]
    public void ApplyRoleChange_NoAudit_WhenSame()
    {
        var membership = new Membership { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), UserId = Guid.NewGuid(), Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow };
        var audit = membership.ApplyRoleChange(Roles.Learner, changedByEmail: "noreply@test", changedByUserId: null);
        Assert.Null(audit);
    }
}
