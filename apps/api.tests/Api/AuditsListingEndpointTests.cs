using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Appostolic.Api.Tests.Api;

public class AuditsListingEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public AuditsListingEndpointTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Lists_audits_with_paging_and_total_count()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Arrange seed: tenant, users, membership and 3 audit rows for same tenant
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var admin = new User { Id = Guid.NewGuid(), Email = $"a-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        var target = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        db.AddRange(tenant, admin, target);
        db.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = admin.Id, Role = MembershipRole.Admin, Roles = Roles.TenantAdmin, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });

        var baseTime = DateTime.UtcNow.AddMinutes(-5);
        db.Audits.AddRange(
            new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator, NewRoles = Roles.Creator | Roles.Approver, ChangedAt = baseTime.AddMinutes(1) },
            new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator | Roles.Approver, NewRoles = Roles.Creator, ChangedAt = baseTime.AddMinutes(2) },
            new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator, NewRoles = Roles.Creator | Roles.Approver | Roles.Learner, ChangedAt = baseTime.AddMinutes(3) }
        );
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-dev-user", admin.Email);
        client.DefaultRequestHeaders.Add("x-tenant", tenant.Name);

        // take=2 should return most recent two (ChangedAt DESC), total=3, then skip=2 returns last one
        var r1 = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?take=2");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r1.Headers.TryGetValues("X-Total-Count", out var totalVals).Should().BeTrue();
        totalVals!.Single().Should().Be("3");
    var p1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
    p1.ValueKind.Should().Be(JsonValueKind.Array);
    p1.GetArrayLength().Should().Be(2);

        var r2 = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?skip=2&take=2");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var p2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        p2.ValueKind.Should().Be(JsonValueKind.Array);
        p2.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Supports_optional_filters_user_actor_and_date_range()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var admin = new User { Id = Guid.NewGuid(), Email = $"a-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        var target1 = new User { Id = Guid.NewGuid(), Email = $"u1-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        var target2 = new User { Id = Guid.NewGuid(), Email = $"u2-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        db.AddRange(tenant, admin, target1, target2);
        db.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = admin.Id, Role = MembershipRole.Admin, Roles = Roles.TenantAdmin, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });

        var baseTime = DateTime.UtcNow.AddHours(-2);
        var a1 = new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target1.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator, NewRoles = Roles.Creator | Roles.Learner, ChangedAt = baseTime.AddMinutes(10) };
        var a2 = new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target2.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator, NewRoles = Roles.Creator | Roles.Approver, ChangedAt = baseTime.AddMinutes(20) };
        var a3 = new Audit { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = target1.Id, ChangedByUserId = admin.Id, ChangedByEmail = admin.Email, OldRoles = Roles.Creator | Roles.Learner, NewRoles = Roles.Creator, ChangedAt = baseTime.AddMinutes(90) };
        db.Audits.AddRange(a1, a2, a3);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-dev-user", admin.Email);
        client.DefaultRequestHeaders.Add("x-tenant", tenant.Name);

        // Filter by target userId
        var rUser = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?userId={target1.Id}");
        rUser.StatusCode.Should().Be(HttpStatusCode.OK);
        var pUser = await rUser.Content.ReadFromJsonAsync<JsonElement>();
        pUser.GetArrayLength().Should().Be(2);

        // Filter by actor (changedByUserId)
        var rActor = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?changedByUserId={admin.Id}");
        rActor.StatusCode.Should().Be(HttpStatusCode.OK);
        var pActor = await rActor.Content.ReadFromJsonAsync<JsonElement>();
        pActor.GetArrayLength().Should().Be(3);

        // Filter by date range (only middle entry a2)
        var from = baseTime.AddMinutes(15).ToString("o");
        var to = baseTime.AddMinutes(30).ToString("o");
        var rRange = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        rRange.StatusCode.Should().Be(HttpStatusCode.OK);
        var pRange = await rRange.Content.ReadFromJsonAsync<JsonElement>();
        pRange.GetArrayLength().Should().Be(1);

        // Invalid range from > to -> 400
        var bad = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?from={Uri.EscapeDataString(to)}&to={Uri.EscapeDataString(from)}");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_400_on_invalid_guid_filters()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"t-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
        var admin = new User { Id = Guid.NewGuid(), Email = $"a-{Guid.NewGuid():N}@ex.com", CreatedAt = DateTime.UtcNow };
        db.AddRange(tenant, admin);
        db.Add(new Membership { Id = Guid.NewGuid(), TenantId = tenant.Id, UserId = admin.Id, Role = MembershipRole.Admin, Roles = Roles.TenantAdmin, Status = MembershipStatus.Active, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("x-dev-user", admin.Email);
        client.DefaultRequestHeaders.Add("x-tenant", tenant.Name);

        // invalid userId guid format
        var rBadUser = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?userId=not-a-guid");
        rBadUser.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // invalid changedByUserId guid format
        var rBadActor = await client.GetAsync($"/api/tenants/{tenant.Id}/audits?changedByUserId=xyz");
        rBadActor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
