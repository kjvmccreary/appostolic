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

    /// <summary>
    /// When true, selected spans (user/tenant profile related) will be enriched with redacted (and, if enabled, hashed) PII attributes.
    /// Defaults false to minimize span cardinality risk. Attributes are never added if hashing emission disabled for hash values.
    /// </summary>
    public bool IncludePIITracing { get; set; } = false;
}
