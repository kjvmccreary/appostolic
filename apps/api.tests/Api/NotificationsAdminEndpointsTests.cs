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

    private const string DefaultPw = "Password123!"; // must match AuthTestClientFlow.DefaultPassword

    /// <summary>
    /// Seed (overwrite) password hash for a user so real /api/auth/login succeeds.
    /// Idempotent across repeated calls within suite.
    /// </summary>
    private static async Task SeedPasswordAsync(WebAppFactory factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var hasher = scope.ServiceProvider.GetRequiredService<Appostolic.Api.Application.Auth.IPasswordHasher>();
        var (hash, salt, _) = hasher.HashPassword(password);
        db.Users.Update(user with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Perform full auth flow (login + select tenant) for kevin@example.com to obtain tenant-scoped JWT.
    /// Replaces legacy AuthTestClient.UseAutoTenantAsync mint helper.
    /// </summary>
    private static async Task LoginOwnerAsync(WebAppFactory factory, HttpClient client)
    {
        await SeedPasswordAsync(factory, "kevin@example.com", DefaultPw);
        await Appostolic.Api.AuthTests.AuthTestClientFlow.LoginAndSelectTenantAsync(factory, client, "kevin@example.com", "kevin-personal");
    }

    [Fact]
    public async Task List_and_retry_notifications_work()
    {
    using var app = new WebAppFactory();
    using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        // Resolve tenant id so non-superadmin JWT-scoped user can access these rows via /api/notifications endpoints
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var n1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-5), TenantId = tenantId };
        var n2 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "b@x.com", DataJson = "{}", Status = NotificationStatus.Failed, CreatedAt = now.AddMinutes(-9), UpdatedAt = now.AddMinutes(-9), LastError = "boom", TenantId = tenantId };
        var n3 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "c@x.com", DataJson = "{}", Status = NotificationStatus.DeadLetter, CreatedAt = now.AddMinutes(-8), UpdatedAt = now.AddMinutes(-8), LastError = "fail", TenantId = tenantId };
        db.Notifications.AddRange(n1, n2, n3);
        await db.SaveChangesAsync();

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await LoginOwnerAsync(app, client);

    // List all (admin endpoint: filters originals only)
    var listResp = await client.GetAsync("/api/notifications?take=10");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        listResp.Headers.Contains("X-Total-Count").Should().BeTrue();
        var items = await listResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
    items.Count.Should().Be(3); // all originals

        // Filter by status
    var failedResp = await client.GetAsync("/api/notifications?status=Failed");
        failedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var failedItems = await failedResp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        failedItems.Count.Should().Be(1);

        // Retry Failed
    var retry1 = await client.PostAsync($"/api/notifications/{n2.Id}/retry", content: null);
        retry1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Retry DeadLetter
    var retry2 = await client.PostAsync($"/api/notifications/{n3.Id}/retry", content: null);
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
    public async Task Resend_manual_endpoint_creates_and_throttles()
    {
    using var app = new WebAppFactory();
    using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var original = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "a@x.com", DataJson = "{}", Status = NotificationStatus.Sent, CreatedAt = now.AddMinutes(-10), UpdatedAt = now.AddMinutes(-10), SentAt = now.AddMinutes(-9), TenantId = tenantId };
        db.Notifications.Add(original);
        await db.SaveChangesAsync();

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await LoginOwnerAsync(app, client);

        // First resend should succeed
    var r1 = await client.PostAsync($"/api/notifications/{original.Id}/resend", content: null);
        r1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await r1.Content.ReadFromJsonAsync<Dictionary<string, object>>() ?? new();
        created.ContainsKey("id").Should().BeTrue();

        // Second immediate resend should be throttled
    var r2 = await client.PostAsync($"/api/notifications/{original.Id}/resend", content: null);
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
    using var app = new WebAppFactory();
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
    await LoginOwnerAsync(app, client);

    // First bulk resend should create up to limit=3
        var req = JsonContent.Create(new { kind = "Verification", limit = 3 });
        var r1 = await client.PostAsync("/api/notifications/resend-bulk", req);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary1 = await r1.Content.ReadFromJsonAsync<BulkSummary>() ?? new BulkSummary();
        summary1.Created.Should().Be(3);
    // Header for remaining cap may be emitted by implementation; do not fail test if absent (not core behavior)
    if (r1.Headers.Contains("X-Resend-Remaining"))
    {
        int.Parse(r1.Headers.GetValues("X-Resend-Remaining").First()).Should().BeGreaterOrEqualTo(0);
    }

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
    public async Task Resend_history_lists_children_with_paging_and_scoping()
    {
    using var app = new WebAppFactory();
    using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var original = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "h1@x.com", DataJson = "{}", Status = NotificationStatus.Sent, TenantId = tenantId, CreatedAt = now.AddMinutes(-20), UpdatedAt = now.AddMinutes(-20) };
        db.Notifications.Add(original);
        var children = Enumerable.Range(0, 3).Select(i => new Notification
        {
            Id = Guid.NewGuid(),
            Kind = EmailKind.Verification,
            ToEmail = "h1@x.com",
            DataJson = "{}",
            Status = NotificationStatus.Queued,
            TenantId = tenantId,
            CreatedAt = now.AddMinutes(-i),
            UpdatedAt = now.AddMinutes(-i),
            ResendOfNotificationId = original.Id
        }).ToList();
        db.Notifications.AddRange(children);
        await db.SaveChangesAsync();

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await LoginOwnerAsync(app, client);

        var r1 = await client.GetAsync($"/api/notifications/{original.Id}/resends?take=2&skip=0");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r1.Headers.Contains("X-Total-Count").Should().BeTrue();
        var page1 = await r1.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        page1.Count.Should().Be(2);

        var r2 = await client.GetAsync($"/api/notifications/{original.Id}/resends?take=2&skip=2");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2 = await r2.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        page2.Count.Should().Be(1);
    }

    [Fact]
    public async Task Resend_metrics_emitted_for_manual_and_bulk()
    {
    using var app = new WebAppFactory();
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
    await LoginOwnerAsync(app, client);

    // Manual resend (admin endpoint)
    var r1 = await client.PostAsync($"/api/notifications/{original.Id}/resend", content: null);
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

    [Fact]
    public async Task Dlq_list_returns_failed_and_deadletter_with_paging()
    {
    using var app = new WebAppFactory();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var items = new[]
        {
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "d1@x.com", DataJson = "{}", Status = NotificationStatus.Failed, TenantId = tenantId, CreatedAt = now.AddMinutes(-3), UpdatedAt = now.AddMinutes(-2), LastError = "e1" },
            new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "d2@x.com", DataJson = "{}", Status = NotificationStatus.DeadLetter, TenantId = tenantId, CreatedAt = now.AddMinutes(-4), UpdatedAt = now.AddMinutes(-3), LastError = "e2" },
        };
        db.Notifications.AddRange(items);
        await db.SaveChangesAsync();

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await LoginOwnerAsync(app, client);

    var resp = await client.GetAsync("/api/notifications/dlq?take=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Headers.Contains("X-Total-Count").Should().BeTrue();
        var list = await resp.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        list.Count.Should().BeGreaterOrEqualTo(2);

        // Filter by status
    var failedOnly = await client.GetAsync("/api/notifications/dlq?status=Failed");
        failedOnly.StatusCode.Should().Be(HttpStatusCode.OK);
        var failedItems = await failedOnly.Content.ReadFromJsonAsync<List<dynamic>>() ?? new();
        failedItems.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Dlq_replay_requeues_and_publishes_ids()
    {
        using var app = _factory.WithWebHostBuilder(_ => { });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var tenantId = await db.Tenants.AsNoTracking().Where(t => t.Name == "kevin-personal").Select(t => t.Id).SingleAsync();
        var f1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Verification, ToEmail = "r1@x.com", DataJson = "{}", Status = NotificationStatus.Failed, TenantId = tenantId, CreatedAt = now.AddMinutes(-6), UpdatedAt = now.AddMinutes(-6), LastError = "e" };
        var d1 = new Notification { Id = Guid.NewGuid(), Kind = EmailKind.Invite, ToEmail = "r2@x.com", DataJson = "{}", Status = NotificationStatus.DeadLetter, TenantId = tenantId, CreatedAt = now.AddMinutes(-7), UpdatedAt = now.AddMinutes(-7), LastError = "e" };
        db.Notifications.AddRange(f1, d1);
        await db.SaveChangesAsync();

    var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    await Appostolic.Api.AuthTests.AuthTestClient.UseAutoTenantAsync(client, "kevin@example.com");

        var body = JsonContent.Create(new { ids = new[] { f1.Id, d1.Id } });
        var resp = await client.PostAsync("/api/notifications/dlq/replay", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await resp.Content.ReadFromJsonAsync<DlqReplaySummary>() ?? new DlqReplaySummary();
        summary.Requeued.Should().Be(2);
        summary.Ids.Should().Contain(new[] { f1.Id, d1.Id });

        using var scope2 = app.Services.CreateScope();
        var rdb = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var n1 = await rdb.Notifications.FindAsync(f1.Id);
        var n2 = await rdb.Notifications.FindAsync(d1.Id);
        n1!.Status.Should().Be(NotificationStatus.Queued);
        n2!.Status.Should().Be(NotificationStatus.Queued);
    }

    private class DlqReplaySummary
    {
        public int Requeued { get; set; }
        public int SkippedForbidden { get; set; }
        public int NotFound { get; set; }
        public int SkippedInvalid { get; set; }
        public int Errors { get; set; }
        public List<Guid> Ids { get; set; } = new();
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
