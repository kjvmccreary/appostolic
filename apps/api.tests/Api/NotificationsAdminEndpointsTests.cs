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

public class NotificationsAdminEndpointsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public NotificationsAdminEndpointsTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_and_retry_notifications_work()
    {
        using var app = _factory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var n1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-5) };
        var n2 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "b@x.com", DataJson = "{}", Status = NotificationStatus.Failed, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), LastError = "boom" };
        var n3 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "c@x.com", DataJson = "{}", Status = NotificationStatus.DeadLetter, CreatedAt = now.AddMinutes(-8), UpdatedAt = now.AddMinutes(-8), LastError = "fail" };
        db.Notifications.AddRange(n1, n2, n3);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // List all
        var listResp = await client.GetAsync("/api/dev/notifications?take=10");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        listResp.Headers.Contains("X-Total-Count").Should().BeTrue();
        var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        items.Count.Should().Be(3);

        // Filter by status
        var failedResp = await client.GetAsync("/api/dev/notifications?status=Failed");
        failedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var failedItems = await failedResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        failedItems.Count.Should().Be(1);

        // Retry Failed
        var retry1 = await client.PostAsync($"/api/dev/notifications/{n2.Id}/retry", content: null);
        retry1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Retry DeadLetter
        var retry2 = await client.PostAsync($"/api/dev/notifications/{n3.Id}/retry", content: null);
        retry2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Verify DB transitions
        using var scope2 = app.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var nn2 = await rdb.Notifications.AsNoTracking().SingleAsync(x => x.Id == n2.Id);
        var nn3 = await rdb.Notifications.AsNoTracking().SingleAsync(x => x.Id == n3.Id);
        nn2!.Status.Should().Be(NotificationStatus.Queued);
        nn3!.Status.Should().Be(NotificationStatus.Queued);
    }

    [Fact]
    public async Task Resend_dev_endpoint_creates_and_throttles()
    {
        using var localFactory = new WebAppFactory();
        using var app = localFactory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var original = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-9) };
        db.Notifications.Add(original);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // First resend should succeed
        var r1 = await client.PostAsync($"/api/dev/notifications/{original.Id}/resend", content: null);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await r1.Content.ReadFromJsonAsync<Dictionary<string, object>>() ?? new();
        created.ContainsKey("id").Should().BeTrue();

        // Second immediate resend should be throttled
        var r2 = await client.PostAsync($"/api/dev/notifications/{original.Id}/resend", content: null);
        r2.StatusCode.Should().Be((HttpStatusCode)429);
        r2.Headers.Contains("Retry-After").Should().BeTrue();

        // Verify DB state: original updated with resend metadata; new item exists
        using var scope2 = app.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var orig = await rdb.Notifications.FindAsync(original.Id);
        orig!.ResendCount.Should().BeGreaterThan(0);
        orig.LastResendAt.Should().NotBeNull();
        var child = await rdb.Notifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).FirstOrDefaultAsync();
        child!.ResendOfNotificationId.Should().Be(original.Id);
        child.Status.Should().Be(NotificationStatus.Queued);
    }

    [Fact]
    public async Task Bulk_resend_creates_with_limit_and_counts_throttled()
    {
        using var localFactory = new WebAppFactory();
        using var app = localFactory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var originals = Enumerable.Range(0, 5).Select(i => new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Verification,
            ToEmail = $"u{i}@x.com",
            DataJson = "{}",
            Status = NotificationStatus.Sent,
            TenantId = tenantId,
            CreatedAt = now.AddMinutes(-10 - i),
            UpdatedAt = now.AddMinutes(-10 - i),
            SentAt = now.AddMinutes(-9 - i)
        }).ToList();
        db.Notifications.AddRange(originals);
        await db.SaveChangesAsync();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

    // First bulk resend should create up to limit=3
        var req = JsonContent.Create(new { kind = "Verification", limit = 3 });
        var r1 = await client.PostAsync("/api/notifications/resend-bulk", req);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary1 = await r1.Content.ReadFromJsonAsync<BulkSummary>() ?? new BulkSummary();
        summary1.Created.Should().Be(3);
    // Header for remaining cap should be present for tenant-scoped request
    r1.Headers.Contains("X-Resend-Remaining").Should().BeTrue();

        // Second bulk resend immediately will encounter throttling for same recipients
        var r2 = await client.PostAsync("/api/notifications/resend-bulk", req);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary2 = await r2.Content.ReadFromJsonAsync<BulkSummary>() ?? new BulkSummary();
        summary2.SkippedThrottled.Should().BeGreaterThan(0);

        // Verify that created children link back to originals
        using var scope2 = app.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var children = await rdb.Notifications.AsNoTracking().Where(n => n.ResendOfNotificationId != null).ToListAsync();
        children.Count.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Resend_metrics_emitted_for_manual_and_bulk()
    {
        using var localFactory = new WebAppFactory();
        using var app = localFactory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var original = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "m1@x.com", DataJson = "{}", Status = NotificationStatus.Sent, TenantId = tenantId, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10) };
        db.Notifications.Add(original);
        await db.SaveChangesAsync();

        // Capture metrics via MeterListener
        var measurements = new List<(string name, double value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var meterListener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Appostolic.Metrics") listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, val, tags, state) =>
        {
            lock (measurements) measurements.Add((inst.Name, val, tags.ToArray()));
        });
        meterListener.Start();

        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("x-dev-user", "kevin@example.com");
        client.DefaultRequestHeaders.Add("x-tenant", "kevin-personal");

        // Manual resend (dev)
        var r1 = await client.PostAsync($"/api/dev/notifications/{original.Id}/resend", content: null);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Bulk resend with throttle (should record throttled outcomes)
        var req = JsonContent.Create(new { kind = "Verification", limit = 1 });
        var r2 = await client.PostAsync("/api/notifications/resend-bulk", req);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Snapshot and assert metrics names observed
        List<(string name, double value, IReadOnlyList<KeyValuePair<string, object?>> tags)> snapshot;
        lock (measurements) snapshot = measurements.ToList();

        snapshot.Any(m => m.name == "email.resend.total" && m.tags.Any(t => t.Key == "mode"))
            .Should().BeTrue();
        snapshot.Any(m => m.name == "email.resend.batch.size").Should().BeTrue();
    }

    private class BulkSummary
    {
        public int Created { get; set; }
        public int SkippedThrottled { get; set; }
        public int SkippedForbidden { get; set; }
        public int NotFound { get; set; }
        public int Errors { get; set; }
        public List<Guid> Ids { get; set; } = new();
    }
}
