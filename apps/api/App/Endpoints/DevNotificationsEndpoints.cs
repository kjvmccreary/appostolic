using Appostolic.Api.App.Notifications;
using Appostolic.Api.App.Options;
using Microsoft.AspNetCore.Authorization;

namespace Appostolic.Api.App.Endpoints;

public static class DevNotificationsEndpoints
{
    public sealed record VerificationRequest(string ToEmail, string? ToName, string Token);
    public sealed record InviteRequest(string ToEmail, string? ToName, string Tenant, string Role, string Inviter, string Token);

    public static void MapDevNotificationsEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        var group = app.MapGroup("/api/dev/notifications").RequireAuthorization();

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
    }
}
