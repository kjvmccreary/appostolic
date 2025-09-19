using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Appostolic.Api; // For AppDbContext and entity types

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Asserts that the legacy Membership.Role column (and enum mapping) still exists during early refactoring stories.
/// This guards against premature removal before the planned Story 7 migration. Failing early provides a clear
/// signal that the decommission sequence was not followed, preserving rollback invariants.
/// Remove this test when executing Story 7 (column + enum drop).
/// </summary>
public class LegacyRoleColumnPresenceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRoleColumnPresenceTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Membership_entity_still_has_Legacy_Role_property_mapped()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.Model.FindEntityType(typeof(Membership));
        entity.Should().NotBeNull();
        var prop = entity!.FindProperty("Role");
        prop.Should().NotBeNull("legacy Role column should remain until Story 7 migration");
        // Validate underlying column name for additional safety (default EF naming expected: role)
        var storeObject = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("memberships", null);
        prop!.GetColumnName(storeObject).Should().Be("role");
    }
}
