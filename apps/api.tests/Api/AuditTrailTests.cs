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

        private static HttpClient Client(WebAppFactory f, string email, string tenantSlug)
        {
            var c = f.CreateClient();
            c.DefaultRequestHeaders.Add("x-dev-user", email);
            c.DefaultRequestHeaders.Add("x-tenant", tenantSlug);
            return c;
        }

        [Fact]
        public async Task Set_roles_writes_audit_row_with_old_and_new_roles()
        {
            var owner = Client(_factory, "kevin@example.com", "kevin-personal");

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
                    Role = MembershipRole.Viewer,
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
                a.ChangedByEmail.Should().Be("kevin@example.com");
                a.ChangedByUserId.Should().NotBeEmpty();
            }
        }
    }
}
