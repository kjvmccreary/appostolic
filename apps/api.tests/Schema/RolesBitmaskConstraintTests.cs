using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Appostolic.Api;

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Verifies the database-level bitmask validity constraint added in migration AddRolesBitmaskConstraint.
/// Ensures only lower 4 bits (1|2|4|8) are permitted and non-zero. An out-of-range bit (e.g. 32) should fail.
/// </summary>
public class RolesBitmaskConstraintTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RolesBitmaskConstraintTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Insert_membership_with_invalid_bitmask_fails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (!db.Database.IsRelational())
        {
            return; // Skip under InMemory provider (constraint not enforced at provider level)
        }

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"bitmask-tenant-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.NewGuid(), Email = $"bitmask-user-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // roles=32 (1<<5) — invalid (outside defined Roles flags)
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            Roles = (Roles)32,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        db.Memberships.Add(membership);

        var act = async () => await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAnyAsync<DbUpdateException>(act);
        ex.InnerException?.Message.Should().Contain("roles_valid");
    }
}
