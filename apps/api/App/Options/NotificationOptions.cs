namespace Appostolic.Api.App.Options;

public sealed class NotificationOptions
{
    // Dedupe TTL (e.g., 10 minutes)
    public TimeSpan DedupeTtl { get; set; } = TimeSpan.FromMinutes(10);

    // Retention windows
    public TimeSpan RetainSentFor { get; set; } = TimeSpan.FromDays(60);
    public TimeSpan RetainFailedFor { get; set; } = TimeSpan.FromDays(90);
    public TimeSpan RetainDeadLetterFor { get; set; } = TimeSpan.FromDays(90);
}
