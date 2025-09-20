using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using Appostolic.Api; // For AppDbContext and entity types

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Verifies the legacy Membership.Role column mapping state.
/// Story 4 decision: we no longer depend on the single 'Role' column for new writes (all writes use Roles flags).
/// If the legacy column mapping has already been removed, we assert its absence to lock in forward progress.
/// If in a future branch we re-introduce the mapping temporarily, update the assertion accordingly.
/// </summary>
public class LegacyRoleColumnPresenceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public LegacyRoleColumnPresenceTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public void Membership_entity_legacy_Role_property_still_mapped_pending_removal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.Model.FindEntityType(typeof(Membership));
        entity.Should().NotBeNull();
        var prop = entity!.FindProperty("Role");
        prop.Should().NotBeNull("conservative path: Role column retained temporarily; update this test when removal migration is executed");
        // Document intent: once Role is fully removed (schema + property), flip this assertion to BeNull and rename test.
    }
}
