using Appostolic.Api.Application.Privacy;

namespace Appostolic.Api.App.Notifications;

/// <summary>
/// Legacy email redactor kept for backward compatibility. Delegates to unified <see cref="PIIRedactor"/>.
/// Will be removed after all call sites migrate directly to PIIRedactor.
/// </summary>
[Obsolete("Use PIIRedactor.RedactEmail instead.")]
public static class EmailRedactor
{
    /// <summary>
    /// Redacts an email address (legacy entry point). See <see cref="PIIRedactor.RedactEmail"/>.
    /// </summary>
    public static string Redact(string email) => PIIRedactor.RedactEmail(email);
}
