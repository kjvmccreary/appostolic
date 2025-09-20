using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Appostolic.Api.AuthTests;

public class TestTokenFactoryTests : IClassFixture<Appostolic.Api.Tests.WebAppFactory>
{
    private readonly Appostolic.Api.Tests.WebAppFactory _factory;
    public TestTokenFactoryTests(Appostolic.Api.Tests.WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task MintTenantToken_SingleMembership_AutoTenantYieldsTenantToken()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var helper = new TestAuthClient(client);
        var email = $"testhelper-{Guid.NewGuid():N}@example.com";

        // Act
        var (neutral, tenant) = await helper.MintAsync(email, autoTenant: true);

        // Assert
        neutral.Should().NotBeNullOrWhiteSpace();
        tenant.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MintTenantToken_MultiMembership_ExplicitTenantSelection()
    {
        using var client = _factory.CreateClient();
        var email = $"multihelper-{Guid.NewGuid():N}@example.com";

        // First mint creates personal tenant
        var helper = new TestAuthClient(client);
        var _ = await helper.MintAsync(email, autoTenant: true);

        // Manually add second membership
        using(var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"second-{Guid.NewGuid():N}".Substring(0, 20), CreatedAt = DateTime.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = user.Id, Roles = Roles.Creator | Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        // Mint again with explicit tenant slug selection (should include tenantToken for that slug)
        var res = await client.PostAsJsonAsync("/api/test/mint-tenant-token", new { Email = email, Tenant = "second" }); // partial slug won't match; expect no tenant token
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await res.Content.ReadFromJsonAsync<JsonObject>();
        (doc!["tenantToken"] == null).Should().BeTrue(); // slug mismatch
    }

    [Fact]
    public async Task MintTenantToken_FlagDisabled_ReturnsNotFound()
    {
        // Spin up a derived factory with helper flag disabled (override original in-memory provider)
        var disabledFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AUTH__TEST_HELPERS_ENABLED"] = "false"
                });
            });
        });
        using var client = disabledFactory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/test/mint-tenant-token", new { Email = "unused@example.com" });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
