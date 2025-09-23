using System.Diagnostics;

namespace Appostolic.Api.Application.Auth;

/*
 Central helper for adding standardized auth tracing attributes and emitting enrichment metrics (Story 5).
 Avoids sprinkling string keys around the codebase and ensures we never leak PII (emails, refresh plaintext).
 Attributes (non-PII):
    - auth.user_id (Guid)
    - auth.tenant_id (Guid) optional
    - auth.outcome (success|failure)
    - auth.reason (bounded machine code e.g., refresh_reuse, refresh_expired) optional on failure
*/
public static class AuthTracing
{
    public static readonly ActivitySource Source = new("Appostolic.Auth");

    /// <summary>
    /// Enrich an existing activity with standardized auth attributes. Safe no-op if activity is null or not recording.
    /// outcome: success|failure. reason optional bounded code (never raw exception/message).
    /// </summary>
    public static void Enrich(Activity? activity, Guid userId, Guid? tenantId, string outcome, string? reason = null)
    {
        if (activity is null || !activity.IsAllDataRequested) return;
        activity.SetTag("auth.user_id", userId);
        if (tenantId.HasValue) activity.SetTag("auth.tenant_id", tenantId.Value);
        activity.SetTag("auth.outcome", outcome);
        if (!string.IsNullOrWhiteSpace(reason)) activity.SetTag("auth.reason", reason);
        // Derive span kind label (server vs internal) for metric dimensions.
        var kind = activity.Kind == ActivityKind.Server ? "server" : "internal";
        // Only attach reason tag for failure spans; metrics track both success/failure counts.
        AuthMetrics.IncrementTraceEnriched(kind, outcome);
    }
}