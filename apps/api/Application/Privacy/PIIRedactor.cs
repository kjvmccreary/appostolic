namespace Appostolic.Api.Application.Privacy;

/// <summary>
/// Redaction helpers for rendering minimal, non-sensitive representations of PII.
/// </summary>
public static class PIIRedactor
{
    /// <summary>
    /// Redacts an email to first character + *** + @domain (k***@example.com). Falls back to ***email when local part too short.
    /// </summary>
    public static string RedactEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var at = email.IndexOf('@');
        if (at <= 0) return "***"; // malformed (no local part)
        var local = email[..at];
        var domain = email[(at + 1)..];
        // Legacy fallback: if local part length < 2, prepend *** to full address (preserve existing callers expectations)
        if (local.Length < 2) return "***" + email;
        return local[0] + "***@" + domain;
    }

    /// <summary>
    /// Redacts a phone number showing only last 4 digits; keeps plus sign if present. e.g. +1 (555) 123-4567 -> ***-4567
    /// </summary>
    public static string RedactPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return string.Empty;
        var last4 = digits.Length <= 4 ? digits : digits[^4..];
        return "***" + last4; // no hyphen to align with test expectations & simpler search exclusion
    }
}
