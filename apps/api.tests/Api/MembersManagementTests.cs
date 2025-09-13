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

    private static HttpClient Client(WebAppFactory f, string email, string tenantSlug)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("x-dev-user", email);
        c.DefaultRequestHeaders.Add("x-tenant", tenantSlug);
        return c;
    }

    [Fact]
    public async Task Owner_can_promote_editor_to_admin()
    {
        var owner = Client(_factory, "kevin@example.com", "kevin-personal");

        // Arrange: create a new user as Editor
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            var email = $"member-{Guid.NewGuid():N}@example.com";
            var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            db.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Role = MembershipRole.Editor,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // Fetch the created member
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            targetUserId = await db.Memberships.AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.Role == MembershipRole.Editor)
                .Select(m => m.UserId)
                .OrderByDescending(_ => _)
                .FirstAsync();
        }

        var resp = await owner.PutAsJsonAsync($"/api/tenants/{tenantId}/members/{targetUserId}", new { role = "Admin" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var m = await db.Memberships.AsNoTracking().FirstAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            m.Role.Should().Be(MembershipRole.Admin);
        }
    }

    [Fact]
    public async Task Admin_cannot_assign_owner_but_owner_can()
    {
        var owner = Client(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid adminUserId;
        Guid targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;

            // Create admin and editor
            var admin = new User { Id = Guid.NewGuid(), Email = $"admin-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            var editor = new User { Id = Guid.NewGuid(), Email = $"editor-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
            db.Users.AddRange(admin, editor);
            db.Memberships.AddRange(
                new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = admin.Id, Role = MembershipRole.Admin, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow },
                new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = editor.Id, Role = MembershipRole.Editor, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
            adminUserId = admin.Id;
            targetUserId = editor.Id;
        }

        string adminEmail;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminEmail = (await db.Users.AsNoTracking().FirstAsync(u => u.Id == adminUserId)).Email;
        }
        var adminClient = Client(_factory, adminEmail, "kevin-personal");
        var tryOwnerResp = await adminClient.PutAsJsonAsync($"/api/tenants/{tenantId}/members/{targetUserId}", new { role = "Owner" });
        tryOwnerResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerResp = await owner.PutAsJsonAsync($"/api/tenants/{tenantId}/members/{targetUserId}", new { role = "Owner" });
        ownerResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // no-op
    }

    [Fact]
    public async Task Cannot_demote_or_remove_last_owner()
    {
        var owner = Client(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid ownerUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t = await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal");
            tenantId = t.Id;
            ownerUserId = (await db.Users.AsNoTracking().FirstAsync(u => u.Email == "kevin@example.com")).Id;
            // Ensure only one owner exists
            var owners = await db.Memberships.Where(m => m.TenantId == tenantId && m.Role == MembershipRole.Owner).ToListAsync();
            if (owners.Count > 1)
            {
                db.Memberships.RemoveRange(owners.Where(o => o.UserId != ownerUserId));
                await db.SaveChangesAsync();
            }
        }

        var demoteResp = await owner.PutAsJsonAsync($"/api/tenants/{tenantId}/members/{ownerUserId}", new { role = "Admin" });
        demoteResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var removeResp = await owner.DeleteAsync($"/api/tenants/{tenantId}/members/{ownerUserId}");
        removeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Remove_non_owner_member()
    {
        var owner = Client(_factory, "kevin@example.com", "kevin-personal");
        Guid tenantId;
        Guid targetUserId;
        string email;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
            email = $"remove-{Guid.NewGuid():N}@example.com";
            var u = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
            db.Users.Add(u);
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = u.Id, Role = MembershipRole.Viewer, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            targetUserId = u.Id;
        }

        var resp = await owner.DeleteAsync($"/api/tenants/{tenantId}/members/{targetUserId}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exists = await db.Memberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == targetUserId);
            exists.Should().BeFalse();
        }
    }
}
