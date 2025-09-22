using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Appostolic.Api.Tests.Api
{
    public class AuditTrailTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public AuditTrailTests(WebAppFactory factory) => _factory = factory;

        /// <summary>
        /// Create a tenant-authenticated client via JWT (replaces legacy dev headers).
        /// </summary>
        private static async Task<HttpClient> ClientAsync(WebAppFactory f, string email, string tenantSlug)
        {
            var c = f.CreateClient();
            await Appostolic.Api.AuthTests.AuthTestClient.UseTenantAsync(c, email, tenantSlug);
            return c;
        }

        private static async Task EnsureAdminMembershipAsync(WebAppFactory f, string email, Guid tenantId)
        {
            using var scope = f.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return;
            var membership = await db.Memberships.FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenantId);
            var full = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
            if (membership == null)
            {
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, Roles = full, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            else if ((membership.Roles & full) != full)
            {
                membership.Roles |= full;
                await db.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task Set_roles_writes_audit_row_with_old_and_new_roles()
        {
            var owner = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");

            Guid tenantId;
            Guid targetUserId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
                // Seed a viewer with no flags
                var u = new User { Id = Guid.NewGuid(), Email = $"aud-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
                db.Users.Add(u);
                db.Memberships.Add(new Membership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = u.Id,
                    Roles = Roles.None,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                targetUserId = u.Id;
            }

            // Defensive: ensure acting user has admin membership flags
            await EnsureAdminMembershipAsync(_factory, "kevin@example.com", tenantId);
            await LogMembershipAsync(_factory, tenantId, "kevin@example.com", nameof(Set_roles_writes_audit_row_with_old_and_new_roles));
            // Act: set flags to TenantAdmin
            var resp = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin" } });
            resp.StatusCode.Should().Be(HttpStatusCode.OK);

            // Assert: one audit row exists with expected values
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var audits = await db.Audits.AsNoTracking().Where(a => a.TenantId == tenantId && a.UserId == targetUserId).OrderBy(a => a.ChangedAt).ToListAsync();
                audits.Should().HaveCount(1);
                var a = audits[0];
                a.OldRoles.Should().Be(Roles.None);
                a.NewRoles.Should().Be(Roles.TenantAdmin);
                a.ChangedByEmail.Should().Be("kevin@example.com");
                a.ChangedByUserId.Should().NotBeEmpty();
            }
        }

        [Fact]
        public async Task Set_roles_noop_second_call_does_not_duplicate_audit()
        {
            var owner = await ClientAsync(_factory, "kevin@example.com", "kevin-personal");

            Guid tenantId;
            Guid targetUserId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                tenantId = (await db.Tenants.AsNoTracking().FirstAsync(t => t.Name == "kevin-personal")).Id;
                // Seed member with no roles
                var u = new User { Id = Guid.NewGuid(), Email = $"aud-noop-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
                db.Users.Add(u);
                db.Memberships.Add(new Membership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = u.Id,
                    Roles = Roles.None,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                targetUserId = u.Id;
            }

            await EnsureAdminMembershipAsync(_factory, "kevin@example.com", tenantId);
            await LogMembershipAsync(_factory, tenantId, "kevin@example.com", nameof(Set_roles_noop_second_call_does_not_duplicate_audit));
            // First change: assign TenantAdmin
            var first = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin" } });
            first.StatusCode.Should().Be(HttpStatusCode.OK);

            // Second call with identical roles should be a noop returning 204
            var second = await owner.PostAsJsonAsync($"/api/tenants/{tenantId}/memberships/{targetUserId}/roles", new { roles = new[] { "TenantAdmin" } });
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var audits = await db.Audits.AsNoTracking().Where(a => a.TenantId == tenantId && a.UserId == targetUserId).ToListAsync();
                audits.Should().HaveCount(1, "a noop second roles assignment must not create an additional audit row");
                audits[0].NewRoles.Should().Be(Roles.TenantAdmin);
            }
        }

        /// <summary>
        /// TEMP DEBUG: writes membership roles for acting user to console to investigate 403s.
        /// </summary>
        private static async Task LogMembershipAsync(WebAppFactory f, Guid tenantId, string email, string context)
        {
            try
            {
                using var scope = f.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
                if (user == null)
                {
                    Console.WriteLine($"[test][roles][debug] ctx={context} email={email} user=NOT_FOUND");
                    return;
                }
                var membership = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == user.Id && m.TenantId == tenantId);
                if (membership == null)
                {
                    Console.WriteLine($"[test][roles][debug] ctx={context} email={email} membership=NOT_FOUND tenant={tenantId}");
                }
                else
                {
                    Console.WriteLine($"[test][roles][debug] ctx={context} email={email} tenant={tenantId} roles={membership.Roles} value={(int)membership.Roles}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[test][roles][debug][error] ctx={context} {ex}");
            }
        }
    }
}
