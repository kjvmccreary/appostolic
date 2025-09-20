using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appostolic.Api.Tests.Api;

/// <summary>
/// Verifies invariants for the flags-only roles model post-removal of the legacy MembershipRole enum.
/// Invariants:
///  - No membership should have Roles == Roles.None (guarded by DB constraint in production).
///  - (Optional future) All TenantAdmin memberships must include Creator|Learner for baseline capability pairing.
/// This replaces the obsolete legacy convergence test that compared legacy Role vs flags.
/// </summary>
public class FlagsIntegrityTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public FlagsIntegrityTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task No_membership_has_zero_roles_bitmask()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zero = await db.Memberships.AsNoTracking().Where(m => m.Roles == Roles.None).CountAsync();
        zero.Should().Be(0, "flags-only model forbids zero roles bitmask; convergence + constraint should ensure this");
    }
}
