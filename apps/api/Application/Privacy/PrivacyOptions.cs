namespace Appostolic.Api.Application.Privacy;

/// <summary>
/// Options controlling PII hashing/redaction behavior.
/// </summary>
public sealed class PrivacyOptions
{
    /// <summary>
    /// Pepper appended to normalized PII before hashing. Optional but recommended in production to reduce rainbow table risk.
    /// </summary>
    public string? PIIHashPepper { get; set; }

    /// <summary>
    /// Master switch: when false, hashing still returns values but hashed derivatives are not emitted into logs/metrics.
    /// Keeps interfaces simple while allowing runtime disable of emission.
    /// </summary>
    public bool PIIHashingEnabled { get; set; } = true;
}
