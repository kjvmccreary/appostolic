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
    /// <summary>
    /// Comma separated list of base64 signing keys (AUTH__JWT__SIGNING_KEYS). First key signs, all keys verify. Overrides SigningKeyBase64 if provided.
    /// </summary>
    public string? SigningKeysBase64Csv { get; set; } // AUTH__JWT__SIGNING_KEYS
    public int AccessTtlMinutes { get; set; } = 15; // AUTH__JWT__ACCESS_TTL_MINUTES
    public int RefreshTtlDays { get; set; } = 30; // AUTH__JWT__REFRESH_TTL_DAYS
    public int ClockSkewSeconds { get; set; } = 60; // AUTH__JWT__CLOCK_SKEW_SECONDS

    /// <summary>
    /// Returns the raw signing key bytes. If no configured key is provided (dev), generates a stable ephemeral key per process.
    /// </summary>
    public byte[] GetSigningKeyBytes()
    {
        var list = GetSigningKeyBytesList();
        return list.Count > 0 ? list[0] : (_ephemeral ??= Guid.NewGuid().ToByteArray());
    }

    /// <summary>
    /// Returns all configured signing key byte arrays (first = active signer). Falls back to single key or ephemeral if none.
    /// </summary>
    public List<byte[]> GetSigningKeyBytesList()
    {
        var results = new List<byte[]>();
        if (!string.IsNullOrWhiteSpace(SigningKeysBase64Csv))
        {
            foreach (var raw in SigningKeysBase64Csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try { results.Add(Convert.FromBase64String(raw)); } catch (FormatException) { /* skip invalid */ }
            }
        }
        else if (!string.IsNullOrWhiteSpace(SigningKeyBase64))
        {
            try { results.Add(Convert.FromBase64String(SigningKeyBase64)); } catch (FormatException) { /* fall through */ }
        }
        if (results.Count == 0)
        {
            results.Add(_ephemeral ??= Guid.NewGuid().ToByteArray());
        }
        return results;
    }

    private byte[]? _ephemeral;
}
