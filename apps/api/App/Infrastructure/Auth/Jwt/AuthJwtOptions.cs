using System;

namespace Appostolic.Api.Infrastructure.Auth.Jwt;

/// <summary>
/// Options controlling JWT issuance and validation. Values come from configuration under AUTH__JWT__*
/// </summary>
public class AuthJwtOptions
{
    public bool Enabled { get; set; } = true; // AUTH__JWT__ENABLED
    public string Issuer { get; set; } = "appostolic-local"; // AUTH__JWT__ISSUER
    public string Audience { get; set; } = "appostolic-api"; // AUTH__JWT__AUDIENCE
    public string? SigningKeyBase64 { get; set; } // AUTH__JWT__SIGNING_KEY (base64 of raw key bytes)
    public int AccessTtlMinutes { get; set; } = 15; // AUTH__JWT__ACCESS_TTL_MINUTES
    public int RefreshTtlDays { get; set; } = 30; // AUTH__JWT__REFRESH_TTL_DAYS
    public int ClockSkewSeconds { get; set; } = 60; // AUTH__JWT__CLOCK_SKEW_SECONDS

    /// <summary>
    /// Returns the raw signing key bytes. If no configured key is provided (dev), generates a stable ephemeral key per process.
    /// </summary>
    public byte[] GetSigningKeyBytes()
    {
        if (!string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            try
            {
                return Convert.FromBase64String(SigningKeyBase64!);
            }
            catch (FormatException)
            {
                // Fall through to generated key
            }
        }
        // For early Story 1 dev convenience; replaced / hardened in later stories (secure persistence, rotation)
        return _ephemeral ??= Guid.NewGuid().ToByteArray();
    }

    private byte[]? _ephemeral;
}
