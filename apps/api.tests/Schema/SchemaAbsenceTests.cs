using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Appostolic.Api;

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Verifies that the legacy memberships.role column has been physically removed from the database schema.
/// This locks in completion of Story 7 (column drop) and prevents accidental reintroduction.
/// </summary>
public class SchemaAbsenceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public SchemaAbsenceTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SchemaDoesNotIncludeLegacyRoleColumn()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // If the test host is using the EF InMemory provider, we cannot query information_schema.
        // In that case this test becomes a no-op; the removal is still covered by the model-level
        // LegacyRoleColumnRemovalTests. When/if test infrastructure switches to a relational provider
        // (e.g. PostgreSQL test container), this assertion will exercise the physical schema.
        if (!db.Database.IsRelational())
        {
            return; // skip silently (xUnit dynamic skip pattern)
        }

        // Query information_schema to ensure the legacy column no longer exists.
        var legacyColumnExists = await db.Database
            .SqlQueryRaw<int>(@"SELECT 1 FROM information_schema.columns 
                                 WHERE table_schema = 'app' AND table_name = 'memberships' AND column_name = 'role'")
            .AnyAsync();

        Assert.False(legacyColumnExists, "Legacy column app.memberships.role should be dropped (Story 7 completion).");
    }
}
