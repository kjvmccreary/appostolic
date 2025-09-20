using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Appostolic.Api; // For AppDbContext and entity types

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Verifies the legacy Membership.Role column mapping has been fully removed.
/// Story 7 (refLeg-07) completion guard: after applying the DropLegacyMembershipRole migration the model should
/// no longer expose a mapped property named 'Role'. This test locks in the removal and prevents accidental
/// re-introduction of the legacy single-role column / property.
/// </summary>
public class LegacyRoleColumnRemovalTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRoleColumnRemovalTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Membership_entity_legacy_Role_property_is_removed()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.Model.FindEntityType(typeof(Membership));
        entity.Should().NotBeNull();
        var prop = entity!.FindProperty("Role");
        prop.Should().BeNull("legacy Role property should be removed after migration DropLegacyMembershipRole (Story 7)");
    }
}
