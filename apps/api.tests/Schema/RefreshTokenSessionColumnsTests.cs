using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Appostolic.Api;

namespace Appostolic.Api.Tests.Schema;

/// <summary>
/// Verifies Story 8 session enumeration columns (fingerprint, last_used_at) exist on app.refresh_tokens.
/// Guards against future schema drift or missing migrations in new environments.
/// </summary>
public class RefreshTokenSessionColumnsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public RefreshTokenSessionColumnsTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RefreshTokens_table_includes_session_columns()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Database.IsRelational()) return; // Skip for InMemory provider

        var cols = await db.Database.SqlQueryRaw<string>(@"SELECT column_name FROM information_schema.columns WHERE table_schema='app' AND table_name='refresh_tokens'").ToListAsync();
        Assert.Contains("fingerprint", cols);
        Assert.Contains("last_used_at", cols);
    }
}
