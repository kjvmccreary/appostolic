using System.Text.Json;
using System.Text.Json.Serialization;
using Appostolic.Api.App.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Endpoints;

public static class NotificationsWebhookEndpoints
{
    private const string HeaderToken = "X-SendGrid-Token";

    public static IEndpointRouteBuilder MapNotificationsWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications/webhook").WithTags("NotificationsWebhook");

        group.MapPost("/sendgrid", HandleSendGridAsync)
            .AllowAnonymous(); // Auth via shared token header

        return app;
    }

    private static async Task<IResult> HandleSendGridAsync(
        HttpRequest request,
        IOptions<App.Options.SendGridOptions> sgOptions,
        INotificationOutbox outbox,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("NotificationsWebhook.SendGrid");
        var configuredToken = sgOptions.Value.WebhookToken ?? string.Empty;
        if (!string.IsNullOrEmpty(configuredToken))
        {
            if (!request.Headers.TryGetValue(HeaderToken, out var provided) || !string.Equals(provided.ToString(), configuredToken, StringComparison.Ordinal))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }

        SendGridEvent[]? events;
        try
        {
            events = await JsonSerializer.DeserializeAsync<SendGridEvent[]>(request.Body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }, ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid SendGrid webhook payload");
            return Results.BadRequest(new { error = "invalid_json" });
        }

        if (events is null || events.Length == 0)
        {
            return Results.Accepted();
        }

        var processed = 0;
        foreach (var ev in events)
        {
            if (ev.CustomArgs is null) continue;
            if (!ev.CustomArgs.TryGetValue("notification_id", out var idObj)) continue;
            if (idObj is null) continue;
            if (!Guid.TryParse(idObj.ToString(), out var id)) continue;

            var ts = ev.Timestamp.HasValue ? DateTimeOffset.FromUnixTimeSeconds(ev.Timestamp.Value) : DateTimeOffset.UtcNow;
            var status = NormalizeStatus(ev.EventType ?? string.Empty);
            var reason = ev.Reason;
            try
            {
                await outbox.UpdateProviderStatusAsync(id, provider: "sendgrid", status, ts, reason, ct);
                processed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update provider status for notification {Id}", id);
            }
        }

        return Results.Accepted($"processed={processed}");
    }

    private static string NormalizeStatus(string eventType)
    {
        return eventType.ToLowerInvariant() switch
        {
            "delivered" => "delivered",
            "bounce" => "bounced",
            "blocked" => "blocked",
            "dropped" => "dropped",
            "spamreport" => "spam",
            "deferred" => "deferred",
            _ => eventType.ToLowerInvariant()
        };
    }

    private sealed class SendGridEvent
    {
        [JsonPropertyName("event")]
        public string? EventType { get; set; }
        public long? Timestamp { get; set; }
        [JsonPropertyName("custom_args")]
        public Dictionary<string, object?>? CustomArgs { get; set; }
        public string? Reason { get; set; }
    }
}
