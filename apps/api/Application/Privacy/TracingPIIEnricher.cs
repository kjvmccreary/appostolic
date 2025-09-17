using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Privacy;

/// <summary>
/// Helper to enrich the current Activity (trace span) with redacted + hashed PII attributes
/// respecting privacy configuration. Raw values are never attached.
/// </summary>
public static class TracingPIIEnricher
{
    /// <summary>
    /// Adds email attributes to the current Activity: user.email.redacted and optionally user.email.hash.
    /// No-op if activity null, email empty, or IncludePIITracing disabled.
    /// </summary>
    public static void AddEmail(Activity? activity, string? email, IPIIHasher hasher, IOptions<PrivacyOptions> options)
    {
        if (activity == null) return;
        if (string.IsNullOrWhiteSpace(email)) return;
        var cfg = options.Value;
        if (!cfg.IncludePIITracing) return;
        var redacted = PIIRedactor.RedactEmail(email);
        activity.SetTag("user.email.redacted", redacted);
        if (cfg.PIIHashingEnabled)
        {
            activity.SetTag("user.email.hash", hasher.HashEmail(email));
        }
    }

    /// <summary>
    /// Adds phone attributes to the current Activity: user.phone.redacted and optionally user.phone.hash.
    /// </summary>
    public static void AddPhone(Activity? activity, string? phone, IPIIHasher hasher, IOptions<PrivacyOptions> options)
    {
        if (activity == null) return;
        if (string.IsNullOrWhiteSpace(phone)) return;
        var cfg = options.Value;
        if (!cfg.IncludePIITracing) return;
        var redacted = PIIRedactor.RedactPhone(phone);
        activity.SetTag("user.phone.redacted", redacted);
        if (cfg.PIIHashingEnabled)
        {
            activity.SetTag("user.phone.hash", hasher.HashPhone(phone));
        }
    }
}