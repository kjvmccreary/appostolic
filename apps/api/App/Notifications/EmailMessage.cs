namespace Appostolic.Api.App.Notifications;

public enum EmailKind
{
    Verification,
    Invite
}

public sealed record EmailMessage(
    EmailKind Kind,
    string ToEmail,
    string? ToName,
    IReadOnlyDictionary<string, object?> Data,
    string? DedupeKey = null
);
