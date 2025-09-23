using System.Net;
using System.Net.Http.Json;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Appostolic.Api.AuthTests; // TestAuthSeeder

namespace Appostolic.Api.Tests.Api;

public class NotificationsProdEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public NotificationsProdEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Tenant_owner_can_list_and_retry_within_tenant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Create dynamic tenant + owner via TestAuthSeeder (deterministic, avoids reliance on pre-seeded data)
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var slug = $"tenant-{Guid.NewGuid():N}".Substring(0, 20);
        var (token, _, tenantId) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slug, owner: true);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var now = DateTimeOffset.UtcNow;
        var n1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-5), TenantId = tenantId };
        var n2 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "b@x.com", DataJson = "{}", Status = NotificationStatus.Failed, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), LastError = "boom", TenantId = tenantId };
        db.Notifications.AddRange(n1, n2);
        await db.SaveChangesAsync();

        // List tenant-scoped
    var listResp = await client.GetAsync("/api/notifications?take=10");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
    items.Count.Should().BeGreaterOrEqualTo(2);

        // Retry Failed
        var retry = await client.PostAsync($"/api/notifications/{n2.Id}/retry", content: null);
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var scope2 = _factory.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var nn2 = await rdb.Notifications.AsNoTracking().SingleAsync(x => x.Id == n2.Id);
        nn2.Status.Should().Be(NotificationStatus.Queued);
    }

    [Fact]
    public async Task Superadmin_can_list_cross_tenant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Simulate a superadmin-like ability by using a neutral token (no tenant) then ensuring multiple tenants exist with notifications.
        // For current API behavior (owner tokens scoped to a tenant), we emulate cross-tenant list by first using tenantA token, then verifying filtering for tenantB specifically.
        var email = $"superadmin-{Guid.NewGuid():N}@example.com";
        var slugA = $"tenant-{Guid.NewGuid():N}".Substring(0,20);
        var slugB = $"tenant-{Guid.NewGuid():N}".Substring(0,20);
    // Inject a superadmin claim to emulate allowlisted behavior (tests bypassing login endpoint must add explicitly)
    var extra = new [] { new System.Security.Claims.Claim("superadmin", "true") };
    var (tokenA, _, tenantA) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slugA, owner: true, extraClaims: extra);
    var (tokenB, _, tenantB) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slugB, owner: true, extraClaims: extra);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);

        var nA = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "a@acme.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, SentAt = DateTimeOffset.UtcNow, TenantId = tenantA };
        var nB = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "x@acme.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, SentAt = DateTimeOffset.UtcNow, TenantId = tenantB };
        db.Notifications.AddRange(nA, nB);
        await db.SaveChangesAsync();

        var listResp = await client.GetAsync("/api/notifications?take=50");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        items.Count.Should().BeGreaterOrEqualTo(1); // at least those from tenantA

        // Filter by tenantId should work for superadmin
        var filtered = await client.GetAsync($"/api/notifications?tenantId={tenantB}");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var fitems = await filtered.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        fitems.Any().Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_owner_can_resend_within_tenant_and_is_forbidden_cross_tenant()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Create two distinct tenants with different owners to test cross-tenant forbid
        var emailA = $"ownerA-{Guid.NewGuid():N}@example.com";
        var slugA = $"tenant-{Guid.NewGuid():N}".Substring(0,20);
        var (tokenA, _, tenantA) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, emailA, slugA, owner: true);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenA);
        var emailB = $"ownerB-{Guid.NewGuid():N}@example.com";
        var slugB = $"tenant-{Guid.NewGuid():N}".Substring(0,20);
        var (_, _, tenantB) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, emailB, slugB, owner: true);

        var now = DateTimeOffset.UtcNow;
        var mine = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "me@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-9), TenantId = tenantA };
        var other = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "them@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), SentAt = now.AddMinutes(-8), TenantId = tenantB };
        db.Notifications.AddRange(mine, other);
        await db.SaveChangesAsync();

        var r1 = await client.PostAsync($"/api/notifications/{mine.Id}/resend", content: null);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);

    var r2 = await client.PostAsync($"/api/notifications/{other.Id}/resend", content: null);
    r2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resend_returns_404_when_missing()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var slug = $"tenant-{Guid.NewGuid():N}".Substring(0,20);
        var (token, _, _) = await TestAuthSeeder.IssueTenantTokenAsync(_factory, email, slug, owner: true);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var missing = Guid.NewGuid();
        var r = await client.PostAsync($"/api/notifications/{missing}/resend", content: null);
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
