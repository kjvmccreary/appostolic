namespace Appostolic.Api.Domain.Notifications;

public sealed record NotificationDedupe
{
    public string DedupeKey { get; set; } = string.Empty; // PK
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
