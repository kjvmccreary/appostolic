using System.Security.Claims;
using Appostolic.Api.App.Notifications;
using Appostolic.Api.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Appostolic.Api.App.Options;

namespace Appostolic.Api.App.Endpoints;

public static class NotificationsAdminEndpoints
{
    public sealed record BulkResendRequest(
        EmailKind? Kind,
        Guid? TenantId,
        List<string>? ToEmails,
        DateTimeOffset? From,
        DateTimeOffset? To,
        int? Limit
    );

    public sealed record BulkResendResult(
        int Created,
        int SkippedThrottled,
        int SkippedForbidden,
        int NotFound,
        int Errors,
        List<Guid> Ids
    );
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
            // Operate only on original notifications (not previously-created resends)
            q = q.Where(n => n.ResendOfNotificationId == null);

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

        // GET /api/notifications/{id}/resends
        group.MapGet("/{id:guid}/resends", async (
            Guid id,
            ClaimsPrincipal user,
            AppDbContext db,
            HttpRequest req,
            HttpResponse resp,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);

            var original = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (original is null) return Results.NotFound();

            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
                if (original.TenantId != tenantId) return Results.Forbid();
            }

            // Paging
            int take = 50;
            int skip = 0;
            if (req.Query.TryGetValue("take", out var takeVals) && int.TryParse(takeVals.ToString(), out var t) && t > 0 && t <= 500)
                take = t;
            if (req.Query.TryGetValue("skip", out var skipVals) && int.TryParse(skipVals.ToString(), out var s) && s >= 0)
                skip = s;

            var q = db.Notifications.AsNoTracking()
                .Where(n => n.ResendOfNotificationId == id)
                .OrderByDescending(n => n.CreatedAt);

            var total = await q.CountAsync(ct);
            var items = await q.Skip(skip).Take(take)
                .Select(n => new NotificationAdminListItem(
                    n.Id, n.Kind, n.ToEmail, n.ToName, n.Status, n.AttemptCount, n.CreatedAt, n.SentAt, n.NextAttemptAt, n.LastError, n.TenantId, n.DedupeKey))
                .ToListAsync(ct);

            resp.Headers.Append("X-Total-Count", total.ToString());
            return Results.Ok(items);
        })
        .WithSummary("Resend history for a notification (tenant-scoped or superadmin)")
        .WithDescription("Lists child notifications created via resend for the given original id, newest first. Supports paging via take/skip.");

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

        // POST /api/notifications/{id}/resend
        group.MapPost("/{id:guid}/resend", async (
            Guid id,
            ClaimsPrincipal user,
            AppDbContext db,
            INotificationOutbox outbox,
            INotificationIdQueue idQueue,
            HttpResponse resp,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var original = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (original is null) return Results.NotFound();

            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });
                if (original.TenantId != tenantId) return Results.Forbid();
            }

            try
            {
                var newId = await outbox.CreateResendAsync(id, reason: "manual", ct);
                await idQueue.EnqueueAsync(newId, ct);
                var location = $"/api/notifications/{newId}";
                EmailMetrics.RecordResend(original.Kind.ToString(), mode: "manual", tenantScope: isSuper ? "superadmin" : "self", outcome: "created");
                return Results.Created(location, new { id = newId });
            }
            catch (ResendThrottledException rte)
            {
                var delta = (int)Math.Ceiling((rte.RetryAfter - DateTimeOffset.UtcNow).TotalSeconds);
                resp.Headers.Append("Retry-After", Math.Max(delta, 1).ToString());
                EmailMetrics.RecordResend(original!.Kind.ToString(), mode: "manual", tenantScope: isSuper ? "superadmin" : "self", outcome: "throttled");
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }
            catch (InvalidResendStateException ex)
            {
                EmailMetrics.RecordResend(original!.Kind.ToString(), mode: "manual", tenantScope: isSuper ? "superadmin" : "self", outcome: "error");
                return Results.Conflict(new { message = ex.Message });
            }
        })
        .WithSummary("Resend a notification (tenant-scoped or superadmin)")
        .WithDescription("Creates a new Queued notification linked to the original, enforcing throttle policy. Returns 201 with Location or 429 with Retry-After.");

        // POST /api/notifications/resend-bulk
        group.MapPost("/resend-bulk", async (
            ClaimsPrincipal user,
            AppDbContext db,
            INotificationOutbox outbox,
            INotificationIdQueue idQueue,
            IOptions<NotificationOptions> options,
            HttpResponse resp,
            BulkResendRequest body,
            CancellationToken ct) =>
        {
            var isSuper = string.Equals(user.FindFirst("superadmin")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            Guid? currentTenantId = null;
            if (!isSuper)
            {
                var tenantIdStr = user.FindFirst("tenant_id")?.Value;
                if (!Guid.TryParse(tenantIdStr, out var t)) return Results.BadRequest(new { error = "invalid tenant" });
                currentTenantId = t;
            }

            var reqLimit = Math.Clamp(body.Limit ?? options.Value.BulkResendMaxPerRequest, 1, options.Value.BulkResendMaxPerRequest);

            // Build query of originals to resend
            IQueryable<Notification> q = db.Notifications.AsNoTracking();
            if (body.Kind is EmailKind k)
            {
                q = q.Where(n => n.Kind == k);
            }
            if (body.From is DateTimeOffset from)
            {
                q = q.Where(n => n.CreatedAt >= from);
            }
            if (body.To is DateTimeOffset to)
            {
                q = q.Where(n => n.CreatedAt <= to);
            }
            if (body.ToEmails is { Count: > 0 })
            {
                var emails = body.ToEmails.Select(e => e.Trim()).Where(e => !string.IsNullOrWhiteSpace(e)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                q = q.Where(n => emails.Contains(n.ToEmail));
            }

            // Tenant scoping
            if (isSuper)
            {
                if (body.TenantId is Guid tid)
                {
                    q = q.Where(n => n.TenantId == tid);
                    currentTenantId = tid; // for cap calculation if provided
                }
            }
            else
            {
                q = q.Where(n => n.TenantId == currentTenantId);
            }

            // Order newest first to prioritize recent issues
            q = q.OrderByDescending(n => n.CreatedAt);

            var originals = await q.Take(reqLimit).Select(n => new { n.Id, n.TenantId, n.ToEmail, n.Kind }).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var throttleWindow = options.Value.ResendThrottleWindow;

            // Per-tenant daily cap check (rolling 24h)
            if (currentTenantId is Guid capTenant)
            {
                var since = DateTimeOffset.UtcNow.AddDays(-1);
                // Count resends created in the last 24h for this tenant (children with ResendOfNotificationId not null)
                var recentResends = await db.Notifications
                    .AsNoTracking()
                    .Where(n => n.TenantId == capTenant && n.ResendOfNotificationId != null && n.CreatedAt >= since)
                    .CountAsync(ct);
                var remaining = Math.Max(0, options.Value.BulkResendPerTenantDailyCap - recentResends);
                // Surface remaining via response header per Notif-30
                resp.Headers.Append("X-Resend-Remaining", remaining.ToString());
                if (remaining == 0)
                {
                    EmailMetrics.RecordResendBatchSize(0, tenantScope: isSuper ? "superadmin" : "self", kind: body.Kind?.ToString());
                    return Results.Ok(new BulkResendResult(0, 0, 0, 0, 0, new()));
                }
                if (originals.Count > remaining)
                {
                    originals = originals.Take(remaining).ToList();
                }
            }

            int created = 0, throttled = 0, forbidden = 0, notFound = 0, errors = 0;
            var ids = new List<Guid>(originals.Count);

            // Record selected size as batch size (pre-throttle outcome)
            EmailMetrics.RecordResendBatchSize(originals.Count, tenantScope: isSuper ? "superadmin" : "self", kind: body.Kind?.ToString());

            foreach (var o in originals)
            {
                // Double-check tenant scoping at item level for safety
                if (!isSuper && o.TenantId != currentTenantId)
                {
                    forbidden++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: "self", outcome: "forbidden");
                    continue;
                }

                // Pre-check throttle by (to_email, kind) within window to avoid unnecessary CreateResend attempts
                var throttledRecent = await db.Notifications.AsNoTracking()
                    .AnyAsync(n => n.ToEmail == o.ToEmail && n.Kind == o.Kind && n.CreatedAt >= now - throttleWindow, ct);
                if (throttledRecent)
                {
                    throttled++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "throttled");
                    continue;
                }
                try
                {
                    var newId = await outbox.CreateResendAsync(o.Id, reason: "bulk", ct);
                    await idQueue.EnqueueAsync(newId, ct);
                    ids.Add(newId);
                    created++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "created");
                }
                catch (ResendThrottledException)
                {
                    throttled++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "throttled");
                }
                catch (InvalidResendStateException)
                {
                    // Treat as error for summary visibility
                    errors++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "error");
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Original disappeared
                    notFound++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "not_found");
                }
                catch
                {
                    errors++;
                    EmailMetrics.RecordResend(o.Kind.ToString(), mode: "bulk", tenantScope: isSuper ? "superadmin" : "self", outcome: "error");
                }
            }

            return Results.Ok(new BulkResendResult(created, throttled, forbidden, notFound, errors, ids));
        })
        .WithSummary("Bulk resend notifications")
        .WithDescription("Resends many notifications by filter with per-request and per-tenant caps; enforces per-recipient throttle and returns a summary.");
    }
}
