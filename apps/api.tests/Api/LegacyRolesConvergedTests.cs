using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;
using Appostolic.Api; // for AppDbContext, Membership, Roles, MembershipRole

namespace Appostolic.Api.Tests.Api;

/// <summary>
/// Verifies that after applying the convergence migration all memberships have non-zero roles bitmask
/// and that the bitmask matches the canonical historical mapping of the legacy Role enum. This protects
/// subsequent stories (dropping legacy column) from silent drift.
/// </summary>
public class LegacyRolesConvergedTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRolesConvergedTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task All_memberships_have_consistent_nonzero_roles_bitmask()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mismatches = await db.Memberships
            .AsNoTracking()
            .Where(m => m.Roles == Roles.None ||
                (m.Role == MembershipRole.Owner && m.Roles != (Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner)) ||
                (m.Role == MembershipRole.Admin && m.Roles != (Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner)) ||
                (m.Role == MembershipRole.Editor && m.Roles != (Roles.Creator | Roles.Learner)) ||
                (m.Role == MembershipRole.Viewer && m.Roles != Roles.Learner))
            .Select(m => new { m.Id, m.Role, Roles = (int)m.Roles })
            .ToListAsync();

        mismatches.Should().BeEmpty("convergence migration should align all legacy role rows to canonical flags and eliminate zero bitmasks");
    }
}
