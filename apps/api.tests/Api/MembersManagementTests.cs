using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api;

public class MembersManagementTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public MembersManagementTests(WebAppFactory factory) => _factory = factory;

    /// <summary>
    /// Create a tenant-authenticated client via JWT (replaces legacy dev header auth helper).
    /// </summary>
    private static async Task<HttpClient> ClientAsync(WebAppFactory f, string email, string tenantSlug, bool? forceAllRoles = null)
    {
        var c = f.CreateClient();
        var (neutral, tenant) = await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, email, tenantSlug, forceAllRoles);
        return c;
    }

    /// <summary>
    /// Ensure that the given user has a membership in the specified tenant with full admin flags.
    /// Defensive step because token mint helper may auto-create a personal tenant first; we need the target shared tenant membership.
    /// </summary>
    private static async Task EnsureAdminMembershipAsync(WebAppFactory f, string email, Guid tenantId)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return; // mint helper should have created
        var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenantId);
        var full = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
        if (membership == null)
        {
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Roles = full,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        else if ((membership.Roles & full) != full)
        {
            membership.Roles |= full;
            await db.SaveChangesAsync();
        }
    }

    // Helper to assert a membership's current Roles flags easily.
    private static async Task<Roles> GetRoles(WebAppFactory factory, Guid tenantId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Memberships.AsNoTracking().Where(m => m.TenantId == tenantId && m.UserId == userId)
            .Select(m => m.Roles).FirstAsync();
    }

    [Fact]
    public async Task TenantAdmin_can_add_TenantAdmin_flag_to_member()
    {
    var adminClient = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");

        Guid tenantId;
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            var email = $"member-{Guid.NewGuid():N}@example.com";
            var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            // Seed without the TenantAdmin flag
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Roles = Roles.Creator | Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            targetUserId = user.Id;
        }

    // Defensive: ensure admin membership flags are present for seeded user before mutation
    await EnsureAdminMembershipAsync(_factory, "kevin@example.com", tenantId);
    // Promote by adding TenantAdmin + Approver (full admin style set)
        var promote = await adminClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin", "Approver", "Creator", "Learner" } });
        promote.StatusCode.Should().Be(HttpStatusCode.OK);

        var flags = await GetRoles(_factory, tenantId, targetUserId);
        flags.Should().Be(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner);
    }

    [Fact]
    public async Task Non_admin_cannot_change_roles()
    {
        Guid tenantId;
        string nonAdminEmail;
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            // Create two non-admin members (Learner only)
            var a = new User { Id = Guid.NewGuid(), Email = $"na1-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            var b = new User { Id = Guid.NewGuid(), Email = $"na2-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.AddRange(a, b);
            db.Memberships.AddRange(
                new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = a.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow },
                new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = b.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
            nonAdminEmail = a.Email!;
            targetUserId = b.Id;
        }

    var nonAdminClient = await ClientAsync(_factory, nonAdminEmail, "kevin-personal", forceAllRoles: false);
        var attempt = await nonAdminClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "Creator" } });
        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cannot_remove_last_TenantAdmin()
    {
    var adminClient = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid adminUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            adminUserId = (await db.Users.AsNoTracking().FirstAsync(u => u.Email == "kevin@example.com")).Id;

            // Ensure only one TenantAdmin exists (remove any others that have the flag)
            var others = await db.Memberships.Where(m => m.TenantId == tenantId && m.UserId != adminUserId && (m.Roles & Roles.TenantAdmin) != 0).ToListAsync();
            if (others.Any())
            {
                db.Memberships.RemoveRange(others);
                await db.SaveChangesAsync();
            }
        }

    // Defensive: ensure admin flags present (in case token minted before membership creation)
    await EnsureAdminMembershipAsync(_factory, "kevin@example.com", tenantId);
    // Attempt to remove TenantAdmin flag via roles endpoint
        var demote = await adminClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{adminUserId}/roles", new { roles = new[] { "Creator", "Learner" } });
        demote.StatusCode.Should().Be(HttpStatusCode.Conflict); // cannot remove last TenantAdmin

        // Attempt to delete the last TenantAdmin
        var delete = await adminClient.DeleteAsync($"/api/tenants/{tenantId}/members/{adminUserId}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Remove_non_admin_member()
    {
    var adminClient = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            var u = new User { Id = Guid.NewGuid(), Email = $"remove-{Guid.NewGuid():N}@example.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = u.Id, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            targetUserId = u.Id;
        }

        var resp = await adminClient.DeleteAsync($"/api/tenants/{tenantId}/members/{targetUserId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exists = await db.Memberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            exists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Remove_TenantAdmin_flag_when_another_admin_exists()
    {
    var adminClient = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid primaryAdminUserId;
        Guid secondAdminUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            primaryAdminUserId = (await db.Users.AsNoTracking().FirstAsync(u => u.Email == "kevin@example.com")).Id;

            // Add a second TenantAdmin
            var other = new User { Id = Guid.NewGuid(), Email = $"admin2-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.Add(other);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = other.Id,
                Roles = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            secondAdminUserId = other.Id;
        }

    await EnsureAdminMembershipAsync(_factory, "kevin@example.com", tenantId);
    // Now removing TenantAdmin flag from primary should succeed
        var demote = await adminClient.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{primaryAdminUserId}/roles", new { roles = new[] { "Approver", "Creator", "Learner" } });
        demote.StatusCode.Should().Be(HttpStatusCode.OK);

        var flags = await GetRoles(_factory, tenantId, primaryAdminUserId);
        (flags & Roles.TenantAdmin).Should().Be(0);
        (flags & (Roles.Approver | Roles.Creator | Roles.Learner)).Should().Be(Roles.Approver | Roles.Creator | Roles.Learner);
    }

}
