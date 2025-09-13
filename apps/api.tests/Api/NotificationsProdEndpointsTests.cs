using System.Net;
using System.Net.Http.Json;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
        using var app = _factory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = db.Memberships.AsNoTracking().Select(m => m.TenantId).First();
        var n1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-5), TenantId = tenantId };
        var n2 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "b@x.com", DataJson = "{}", Status = NotificationStatus.Failed, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), LastError = "boom", TenantId = tenantId };
        db.Notifications.AddRange(n1, n2);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // List tenant-scoped
    var listResp = await client.GetAsync("/api/notifications?take=10");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
    var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
    items.Count.Should().BeGreaterOrEqualTo(2);

        // Retry Failed
        var retry = await client.PostAsync($"/api/notifications/{n2.Id}/retry", content: null);
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope2 = app.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var nn2 = await rdb.Notifications.AsNoTracking().SingleAsync(x => x.Id == n2.Id);
        nn2.Status.Should().Be(NotificationStatus.Queued);
    }

    [Fact]
    public async Task Superadmin_can_list_cross_tenant()
    {
        using var app = _factory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed another tenant + notification
        var otherTenant = new Tenant { Id = Guid.NewGuid(), Name = "acme", CreatedAt = DateTime.UtcNow };
        db.Add(otherTenant);
        var nOther = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "x@acme.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, SentAt = DateTimeOffset.UtcNow, TenantId = otherTenant.Id };
        db.Notifications.Add(nOther);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        client.DefaultRequestHeaders.Add("x-superadmin", "true");

        var listResp = await client.GetAsync("/api/notifications?take=50");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        items.Count.Should().BeGreaterThan(0);

        // Filter by tenantId should work for superadmin
        var filtered = await client.GetAsync($"/api/notifications?tenantId={otherTenant.Id}");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var fitems = await filtered.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        fitems.Any().Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_owner_can_resend_within_tenant_and_is_forbidden_cross_tenant()
    {
        using var app = _factory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = db.Memberships.AsNoTracking().Select(m => m.TenantId).First();
        var mine = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "me@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-9), TenantId = tenantId };
        var otherTenant = new Tenant { Id = Guid.NewGuid(), Name = "else", CreatedAt = DateTime.UtcNow };
        var other = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "them@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), SentAt = now.AddMinutes(-8), TenantId = otherTenant.Id };
        db.Add(otherTenant);
        db.Notifications.AddRange(mine, other);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        var r1 = await client.PostAsync($"/api/notifications/{mine.Id}/resend", content: null);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);

        var r2 = await client.PostAsync($"/api/notifications/{other.Id}/resend", content: null);
        r2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Resend_returns_404_when_missing()
    {
        using var app = _factory.WithWebHostBuilder(_ => { });
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");
        var missing = Guid.NewGuid();
        var r = await client.PostAsync($"/api/notifications/{missing}/resend", content: null);
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
