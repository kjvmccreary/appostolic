namespace Appostolic.Api.Tests.TestUtilities;

/// <summary>
/// Centralized short unique ID helpers for test data seeding, replacing multiple ad-hoc
/// UniqueFrag / UniqueSlug / UniqueEmail local functions previously scattered across suites.
/// Provides low-collision, human-inspectable fragments without over-engineering (tests only).
/// </summary>
public static class UniqueId
{
    /// <summary>
    /// Returns a lowercase hex fragment of the specified length (default 8, max 16) derived
    /// from a Guid. Not cryptographically secure; sufficient for test uniqueness.
    /// </summary>
    public static string Frag(int length = 8)
    {
        if (length is <= 0 or > 16) throw new ArgumentOutOfRangeException(nameof(length));
        var hex = Guid.NewGuid().ToString("N");
        return hex[..length];
    }

    /// <summary>
    /// Builds a slug composed of an optional sanitized prefix and a unique fragment.
    /// </summary>
    public static string Slug(string? prefix = null, int fragLength = 8)
    {
        var frag = Frag(fragLength);
        if (string.IsNullOrWhiteSpace(prefix)) return frag;
        var clean = Sanitize(prefix!);
        return string.IsNullOrEmpty(clean) ? frag : $"{clean}-{frag}";
    }

    /// <summary>
    /// Returns a unique email with optional user prefix and configurable domain.
    /// Default domain is example.com to avoid accidental outbound attempts.
    /// </summary>
    public static string Email(string? userPrefix = null, string domain = "example.com", int fragLength = 8)
    {
        var basePart = string.IsNullOrWhiteSpace(userPrefix) ? Frag(fragLength) : $"{Sanitize(userPrefix!)}+{Frag(fragLength)}";
        return $"{basePart}@{domain}".ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            else if (c == '-' || c == '_' || c == ' ') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
