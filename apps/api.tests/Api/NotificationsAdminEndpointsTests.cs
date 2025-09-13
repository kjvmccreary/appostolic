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
}
