using System;

namespace Appostolic.Api.Infrastructure.Auth.Jwt;

/// <summary>
/// Represents a persisted (hashed) refresh token. Neutral in Story 2; future stories may add tenant-scoped purpose.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!; // SHA256 hash (base64) of the refresh token
    public string Purpose { get; set; } = "neutral"; // neutral | tenant (future)
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Metadata { get; set; } // JSON (device, ip, user-agent) future use
}
