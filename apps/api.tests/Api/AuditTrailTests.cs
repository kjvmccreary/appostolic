using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Appostolic.Api.AuthTests; // TestAuthSeeder
using Appostolic.Api.Tests.TestUtilities;

namespace Appostolic.Api.Tests.Api
{
    public class AuditTrailTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public AuditTrailTests(WebAppFactory factory) => _factory = factory;

        // Story: JWT auth refactor – migrate audit trail tests to deterministic TestAuthSeeder
        // approach. We mint an owner tenant-scoped token (grants full role flags) for a unique
        // email + tenant slug per test, removing reliance on pre-seeded "kevin@example.com" and
        // password login/select flows. Each test seeds a target user membership then exercises
        // the roles update endpoint and inspects audit rows.

    // Duplicated Unique* helpers removed in favor of UniqueId

        private async Task<(HttpClient client, Guid tenantId, string actingEmail)> CreateOwnerClientAsync(string scenario)
        {
            var email = UniqueId.Email("audit");
            var slug = UniqueId.Slug(scenario);
            var (token, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slug, owner: true);
            // Optional: seed password hash (not required for token auth but keeps consistency for potential future flows)
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
                var (hash, salt, _) = hasher.HashPassword(TestAuthSeeder.DefaultPassword);
                db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (client, tenantId, email);
        }

        [Fact]
        public async Task Set_roles_writes_audit_row_with_old_and_new_roles()
        {
            var (owner, tenantId, actingEmail) = await CreateOwnerClientAsync("audit-setroles");

            Guid targetUserId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
                a.ChangedByEmail.Should().Be(actingEmail);
                a.ChangedByUserId.Should().NotBeEmpty();
            }
        }

        [Fact]
        public async Task Set_roles_noop_second_call_does_not_duplicate_audit()
        {
            var (owner, tenantId, _) = await CreateOwnerClientAsync("audit-noop");

            Guid targetUserId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

        // Removed legacy debug logging helper; deterministic seeding eliminates previous 403 race conditions.
    }
}
