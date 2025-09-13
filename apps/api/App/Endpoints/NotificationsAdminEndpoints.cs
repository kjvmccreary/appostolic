using System.Security.Claims;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class NotificationsAdminEndpoints
{
    public sealed record NotificationAdminListItem(
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

    public static void MapNotificationsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization().WithTags("Notifications");

        // GET /api/notifications?status=&kind=&tenantId=&take=&skip=
        group.MapGet(string.Empty, async (
            ClaimsPrincipal user,
            AppDbContext db,
            HttpRequest req,
            HttpResponse resp,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

            IQueryable<Notification> q = db.Notifications.AsNoTracking();

            // Tenant scoping: non-superadmin must be limited to current tenant
            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
                q = q.Where(n => n.TenantId == tenantId);
            }

            // Filters
            if (req.Query.TryGetValue("status", out var statusVals) && Enum.TryParse<NotificationStatus>(statusVals.ToString(), true, out var status))
            {
                q = q.Where(n => n.Status == status);
            }
            if (req.Query.TryGetValue("kind", out var kindVals) && Enum.TryParse<EmailKind>(kindVals.ToString(), true, out var kind))
            {
                q = q.Where(n => n.Kind == kind);
            }
            if (isSuper && req.Query.TryGetValue("tenantId", out var tVals) && Guid.TryParse(tVals.ToString(), out var filterTenant))
            {
                q = q.Where(n => n.TenantId == filterTenant);
            }

            q = q.OrderByDescending(n => n.CreatedAt);

            // Paging
            int take = 50;
            int skip = 0;
            if (req.Query.TryGetValue("take", out var takeVals) && int.TryParse(takeVals.ToString(), out var t) && t > 0 && t <= 500)
                take = t;
            if (req.Query.TryGetValue("skip", out var skipVals) && int.TryParse(skipVals.ToString(), out var s) && s >= 0)
                skip = s;

            var total = await q.CountAsync(ct);
            var items = await q.Skip(skip).Take(take)
                .Select(n => new NotificationAdminListItem(
                    n.Id, n.Kind, n.ToEmail, n.ToName, n.Status, n.AttemptCount, n.CreatedAt, n.SentAt, n.NextAttemptAt, n.LastError, n.TenantId, n.DedupeKey))
                .ToListAsync(ct);

            resp.Headers.Append("X-Total-Count", total.ToString());
            return Results.Ok(items);
        })
        .WithSummary("List notifications (tenant-scoped or superadmin)");

        // GET /api/notifications/{id}
        group.MapGet("/{id:guid}", async (
            Guid id,
            ClaimsPrincipal user,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (n is null) return Results.NotFound();

            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
                if (n.TenantId != tenantId) return Results.Forbid();
            }

            return Results.Ok(n);
        })
        .WithSummary("Notification details (tenant-scoped or superadmin)");

        // POST /api/notifications/{id}/retry
        group.MapPost("/{id:guid}/retry", async (
            Guid id,
            ClaimsPrincipal user,
            AppDbContext db,
            INotificationOutbox outbox,
            INotificationIdQueue idQueue,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (n is null) return Results.NotFound();

            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
                if (n.TenantId != tenantId) return Results.Forbid();
            }

            if (n.Status != NotificationStatus.DeadLetter && n.Status != NotificationStatus.Failed)
            {
                return Results.Conflict(new { message = $"Cannot retry notification in status {n.Status}" });
            }

            var ok = await outbox.TryRequeueAsync(id, ct);
            if (!ok) return Results.NotFound();
            await idQueue.EnqueueAsync(id, ct);
            return Results.Accepted();
        })
        .WithSummary("Retry a failed/dead-letter notification (tenant-scoped or superadmin)");
    }
}
