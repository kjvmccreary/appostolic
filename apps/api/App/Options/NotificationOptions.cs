namespace Appostolic.Api.App.Options;

public sealed class NotificationOptions
{
    // Dedupe TTL (e.g., 10 minutes)
    public TimeSpan DedupeTtl { get; set; } = TimeSpan.FromMinutes(10);

    // Retention windows
    public TimeSpan RetainSentFor { get; set; } = TimeSpan.FromDays(60);
    public TimeSpan RetainFailedFor { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan RetainDeadLetterFor { get; set; } = TimeSpan.FromDays(90);

    // PII-aware scrubbing (optional): before delete, scrub sensitive fields after a shorter window
    public bool PiiScrubEnabled { get; set; } = true;
    public TimeSpan ScrubSentAfter { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ScrubFailedAfter { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan ScrubDeadLetterAfter { get; set; } = TimeSpan.FromDays(60);
    // Which fields to scrub (defaults avoid scrubbing email address unless explicitly enabled)
    public bool ScrubToName { get; set; } = true;
    public bool ScrubSubject { get; set; } = true;
    public bool ScrubBodyHtml { get; set; } = true;
    public bool ScrubBodyText { get; set; } = true;
    public bool ScrubToEmail { get; set; } = false;

    // Field encryption (optional; default off)
    // When enabled and a valid key is provided, selected fields are stored encrypted at rest using AES-GCM.
    public bool EncryptFields { get; set; } = false;
    // Base64-encoded key for AES-GCM (16/24/32 bytes after decode). Example: generate 32 bytes and base64 it.
    public string? EncryptionKeyBase64 { get; set; }
    // Which fields to encrypt
    public bool EncryptSubject { get; set; } = false;
    public bool EncryptBodyHtml { get; set; } = false;
    public bool EncryptBodyText { get; set; } = false;
    public bool EncryptToName { get; set; } = false;

    // Resend throttling (Notif-28)
    // Minimum time window between resends for the same (to_email, kind)
    public TimeSpan ResendThrottleWindow { get; set; } = TimeSpan.FromMinutes(5);

    // Bulk resend caps (Notif-29)
    // Max items processed in a single bulk request (defensive per-run cap)
    public int BulkResendMaxPerRequest { get; set; } = 100;
    // Per-tenant daily cap for resends (rolling 24h window)
    public int BulkResendPerTenantDailyCap { get; set; } = 500;
}
