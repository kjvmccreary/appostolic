using System.Text.Json;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Appostolic.Api.Tests.TestUtilities;

/// <summary>
/// Test-specific DbContext that maps JsonDocument properties to string when using the InMemory provider.
/// This avoids provider type-mapping errors for JsonDocument in EFCore.InMemory.
/// </summary>
public class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

    // Use static helpers to avoid unsupported constructs in expression trees
    Expression<Func<JsonDocument?, string?>> toStringExpr = v => SerializeNullable(v);
    Expression<Func<string?, JsonDocument?>> toJsonExpr = s => ParseNullable(s);
        var jsonConverter = new ValueConverter<JsonDocument?, string?>(toStringExpr, toJsonExpr);

        // Apply conversions for JsonDocument properties so InMemory can store them as strings
        modelBuilder.Entity<Tenant>().Property(x => x.Settings).HasConversion(jsonConverter);
        modelBuilder.Entity<User>().Property(x => x.Profile).HasConversion(jsonConverter);
    }

    private static string? SerializeNullable(JsonDocument? doc)
        => doc == null ? null : doc.RootElement.GetRawText();

    private static JsonDocument? ParseNullable(string? s)
        => s == null ? null : JsonDocument.Parse(s);
}
