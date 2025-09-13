namespace Appostolic.Api.App.Options;

public sealed class NotificationOptions
{
    // Dedupe TTL (e.g., 10 minutes)
    public TimeSpan DedupeTtl { get; set; } = TimeSpan.FromMinutes(10);

    // Retention windows
    public TimeSpan RetainSentFor { get; set; } = TimeSpan.FromDays(60);
    public TimeSpan RetainFailedFor { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan RetainDeadLetterFor { get; set; } = TimeSpan.FromDays(90);

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
}
