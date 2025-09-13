using System;
using Appostolic.Api.App.Notifications;

namespace Appostolic.Api.Domain.Notifications;

public enum NotificationStatus
{
    Queued,
    Sending,
    Sent,
    Failed,
    DeadLetter
}

public sealed record Notification
{
    public Guid Id { get; set; }

    // What kind of email this is (Verification, Invite, ...)
    public EmailKind Kind { get; set; }

    // Recipient
    public string ToEmail { get; set; } = string.Empty;
    public string? ToName { get; set; }

    // Snapshotted content at send-time
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public string? BodyText { get; set; }

    // Original template data as JSON (jsonb in DB)
    public string DataJson { get; set; } = "{}";

    // Hashed token (PII minimization) — raw tokens are never stored
    public string? TokenHash { get; set; }

    // Optional tenant correlation
    public Guid? TenantId { get; set; }

    // Dedupe key to prevent duplicates across restarts
    public string? DedupeKey { get; set; }

    // Resend metadata (Notif-27)
    public Guid? ResendOfNotificationId { get; set; }
    public string? ResendReason { get; set; }
    public int ResendCount { get; set; } = 0;
    public DateTimeOffset? LastResendAt { get; set; }
    public DateTimeOffset? ThrottleUntil { get; set; }

    // State machine
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public short AttemptCount { get; set; } = 0;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}
