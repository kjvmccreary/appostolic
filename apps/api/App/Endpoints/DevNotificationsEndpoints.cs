using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Appostolic.Api.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using System.Collections.Generic;

namespace Appostolic.Api.App.Endpoints;

public static class DevNotificationsEndpoints
{
    public sealed record VerificationRequest(string ToEmail, string? ToName, string Token);
    public sealed record InviteRequest(string ToEmail, string? ToName, string Tenant, string Role, string Inviter, string Token);
    public sealed record NotificationListItem(
        Guid Id,
        EmailKind Kind,
        string ToEmail,
        string? ToName,
        NotificationStatus Status,
        short AttemptCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset? SentAt,
        DateTimeOffset? NextAttemptAt,
        string? LastError,
        Guid? TenantId,
        string? DedupeKey
    );

    public static void MapDevNotificationsEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        var group = app.MapGroup("/api/dev/notifications").RequireAuthorization().WithTags("DevNotifications");

        // GET /api/dev/notifications/health — exposes transport mode and (when redis) subscriber state
        group.MapGet("/health", (
            Microsoft.Extensions.Options.IOptions<Appostolic.Api.App.Options.NotificationTransportOptions> transportOptions,
            Appostolic.Api.App.Notifications.RedisTransportDiagnostics diag) =>
        {
            var mode = (transportOptions.Value.Mode ?? "channel").ToLowerInvariant();
            object body = mode == "redis"
                ? new
                {
                    mode,
                    redis = new
                    {
                        enabled = diag.Enabled,
                        subscribed = diag.Subscribed,
                        channel = diag.Channel,
                        lastReceivedAt = diag.LastReceivedAt,
                        receivedCount = diag.ReceivedCount
                    }
                }
                : new { mode };
            return Results.Ok(body);
        }).WithSummary("Dev: notifications transport health");

        // POST /api/dev/notifications/ping — creates a synthetic outbox row and publishes it via the transport for E2E ping
        group.MapPost("/ping", async (
            INotificationOutbox outbox,
            INotificationTransport transport,
            CancellationToken ct) =>
        {
            // Create a minimal synthetic notification row (Queued) that the dispatcher will pick up.
            var msg = new EmailMessage(
                EmailKind.Verification,
                "dev-ping@appostolic.local",
                null,
                new Dictionary<string, object?> { ["ping"] = true }
            );
            var id = await outbox.CreateQueuedAsync(msg, ct);
            await transport.PublishQueuedAsync(id, ct);
            return Results.Accepted($"/api/dev/notifications/{id}", new { id });
        }).WithSummary("Dev: ping the notifications transport");

        group.MapPost("/verification", async (
            INotificationEnqueuer enqueuer,
            VerificationRequest body,
            CancellationToken ct) =>
        {
            await enqueuer.QueueVerificationAsync(body.ToEmail, body.ToName, body.Token, ct);
            return Results.Accepted();
        }).WithSummary("Dev: enqueue verification email");

        group.MapPost("/invite", async (
            INotificationEnqueuer enqueuer,
            InviteRequest body,
            CancellationToken ct) =>
        {
            await enqueuer.QueueInviteAsync(body.ToEmail, body.ToName, body.Tenant, body.Role, body.Inviter, body.Token, ct);
            return Results.Accepted();
        }).WithSummary("Dev: enqueue invite email");

        // GET /api/dev/notifications?kind=&status=&tenantId=&take=&skip=
        group.MapGet("", async (
            AppDbContext db,
            HttpRequest req,
            HttpResponse resp,
            CancellationToken ct) =>
        {
            var q = db.Notifications.AsNoTracking().OrderByDescending(n => n.CreatedAt).AsQueryable();

            // Parse filters
            if (req.Query.TryGetValue("kind", out var kindVals) && !StringValues.IsNullOrEmpty(kindVals))
            {
                var kindStr = kindVals.ToString();
                if (Enum.TryParse<EmailKind>(kindStr, ignoreCase: true, out var kind))
                {
                    q = q.Where(n => n.Kind == kind);
                }
            }
            if (req.Query.TryGetValue("status", out var statusVals) && !StringValues.IsNullOrEmpty(statusVals))
            {
                var statusStr = statusVals.ToString();
                if (Enum.TryParse<NotificationStatus>(statusStr, ignoreCase: true, out var status))
                {
                    q = q.Where(n => n.Status == status);
                }
            }
            // Support both tenant and tenantId for convenience
            Guid tenantId;
            if ((req.Query.TryGetValue("tenantId", out var tVals) && Guid.TryParse(tVals.ToString(), out tenantId))
                || (req.Query.TryGetValue("tenant", out var t2Vals) && Guid.TryParse(t2Vals.ToString(), out tenantId)))
            {
                q = q.Where(n => n.TenantId == tenantId);
            }

            // Paging
            int take = 50;
            int skip = 0;
            if (req.Query.TryGetValue("take", out var takeVals) && int.TryParse(takeVals.ToString(), out var t) && t > 0 && t <= 500)
            {
                take = t;
            }
            if (req.Query.TryGetValue("skip", out var skipVals) && int.TryParse(skipVals.ToString(), out var s) && s >= 0)
            {
                skip = s;
            }

            var total = await q.CountAsync(ct);
            var items = await q.Skip(skip).Take(take)
                .Select(n => new NotificationListItem(
                    n.Id, n.Kind, n.ToEmail, n.ToName, n.Status, n.AttemptCount, n.CreatedAt, n.SentAt, n.NextAttemptAt, n.LastError, n.TenantId, n.DedupeKey))
                .ToListAsync(ct);

            resp.Headers.Append("X-Total-Count", total.ToString());
            return Results.Ok(items);
        })
        .WithSummary("Dev: list notifications")
        .WithDescription("Development-only. Lists notifications with optional filters and paging.");

        // POST /api/dev/notifications/{id}/retry
        group.MapPost("/{id:guid}/retry", async (
            Guid id,
            AppDbContext db,
            INotificationOutbox outbox,
            INotificationTransport transport,
            CancellationToken ct) =>
        {
            var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (n == null) return Results.NotFound();
            if (n.Status != NotificationStatus.DeadLetter && n.Status != NotificationStatus.Failed)
            {
                return Results.Conflict(new { message = $"Cannot retry notification in status {n.Status}" });
            }

            var ok = await outbox.TryRequeueAsync(id, ct);
            if (!ok) return Results.NotFound();
            await transport.PublishQueuedAsync(id, ct);
            return Results.Accepted();
        })
        .WithSummary("Dev: retry a failed/dead-letter notification")
        .WithDescription("Transitions Failed/DeadLetter back to Queued and nudges the dispatcher.");

        // POST /api/dev/notifications/{id}/resend
        group.MapPost("/{id:guid}/resend", async (
            Guid id,
            AppDbContext db,
            INotificationOutbox outbox,
            INotificationTransport transport,
            HttpResponse resp,
            CancellationToken ct) =>
        {
            var original = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (original is null) return Results.NotFound();

            try
            {
                var newId = await outbox.CreateResendAsync(id, reason: "manual", ct);
                await transport.PublishQueuedAsync(newId, ct);
                var location = $"/api/dev/notifications/{newId}";
                EmailMetrics.RecordResend(original.Kind.ToString(), mode: "manual", tenantScope: "dev", outcome: "created");
                return Results.Created(location, new { id = newId });
            }
            catch (ResendThrottledException rte)
            {
                var delta = (int)Math.Ceiling((rte.RetryAfter - DateTimeOffset.UtcNow).TotalSeconds);
                resp.Headers.Append("Retry-After", Math.Max(delta, 1).ToString());
                EmailMetrics.RecordResend(original!.Kind.ToString(), mode: "manual", tenantScope: "dev", outcome: "throttled");
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            catch (InvalidResendStateException ex)
            {
                EmailMetrics.RecordResend(original!.Kind.ToString(), mode: "manual", tenantScope: "dev", outcome: "error");
                return Results.Conflict(new { message = ex.Message });
            }
        })
        .WithSummary("Dev: resend a notification")
        .WithDescription("Clones the notification, links to original, applies throttle, and enqueues");
    }
}
