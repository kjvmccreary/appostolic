using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
        // Modernized: use direct seeding + issuance instead of /api/test/mint-tenant-token for deterministic coverage.
        var email = $"testhelper-{Guid.NewGuid():N}@example.com";
        var (tenantToken, userId, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, $"personal-{Guid.NewGuid():N}".Substring(0, 20));
        tenantToken.Should().NotBeNullOrWhiteSpace();
        userId.Should().NotBe(Guid.Empty);
        tenantId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task MintTenantToken_MultiMembership_ExplicitTenantSelection()
    {
        // Using seeder to create two tenants + memberships and verifying issuance only occurs for exact slug.
        var email = $"multihelper-{Guid.NewGuid():N}@example.com";
        var (_, userId, firstTenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, $"primary-{Guid.NewGuid():N}".Substring(0, 20));
        var secondSlug = $"second-{Guid.NewGuid():N}".Substring(0, 20);
        // Ensure second membership
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.Name == secondSlug))
            {
                var t = new Tenant { Id = Guid.NewGuid(), Name = secondSlug, CreatedAt = DateTime.UtcNow };
                db.Tenants.Add(t);
                await db.SaveChangesAsync();
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), TenantId = t.Id, UserId = userId, Roles = Roles.Learner, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        // Attempt to issue token with non-matching partial slug should yield a neutral-only token when using legacy endpoint behavior.
        // Here we just assert that requesting an incorrect slug via raw endpoint returns no tenant token shape (maintain backwards coverage).
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/test/mint-tenant-token", new { Email = email, Tenant = "second" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await res.Content.ReadFromJsonAsync<JsonObject>();
        (doc!["tenantToken"] == null).Should().BeTrue();
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

    [Fact]
    public async Task MintTenantToken_AllowsAccessLifetimeOverride()
    {
        using var client = _factory.CreateClient();
        var email = $"ttl-{Guid.NewGuid():N}@example.com";
        var result = await AuthTestClient.MintDetailedAsync(client, email, autoTenant: true, neutralAccessTtlMinutes: 1, tenantAccessTtlMinutes: -5);

        result.Neutral.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(10));
        result.Tenant.Should().NotBeNull();
        result.Tenant!.ExpiresAt.Should().BeBefore(DateTimeOffset.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Tenant.Token);
        jwt.ValidTo.Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task MintTenantToken_ExpiredRefreshTokenReturns401()
    {
        using var client = _factory.CreateClient();
        var email = $"refresh-expired-{Guid.NewGuid():N}@example.com";
        var result = await AuthTestClient.MintDetailedAsync(client, email, refreshTtlMinutes: -1);

        result.Refresh.Token.Should().NotBeNullOrWhiteSpace();
        result.Refresh.ExpiresAt.Should().BeBefore(DateTimeOffset.UtcNow);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", $"rt={result.Refresh.Token}");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    var body = await response.Content.ReadFromJsonAsync<JsonObject>();
    body!["code"]!.GetValue<string>().Should().Be("refresh_expired");
    }
}
