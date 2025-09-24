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
    /// <summary>
    /// Story 11: The original creation time of the session chain. For the initial token this equals CreatedAt. Preserved across rotations to enforce absolute max lifetime.
    /// </summary>
    public DateTime? OriginalCreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? Metadata { get; set; } // JSON (device, ip, user-agent) future use
    /// <summary>
    /// Story 8: Optional stable device/browser fingerprint provided by client (e.g., header X-Session-Fp) or derived hash of user-agent.
    /// Used for session enumeration UI grouping. Not security-sensitive.
    /// </summary>
    public string? Fingerprint { get; set; }
    /// <summary>
    /// Story 8: Last time this refresh token was used to obtain a new access token (rotation or tenant selection). Null until first use post-login.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }
    /// <summary>
    /// Story 17/18: Optional human readable device display name provided by client header X-Session-Device.
    /// Informational only; used in session enumeration UI to help users distinguish simultaneous sessions.
    /// </summary>
    public string? DeviceName { get; set; }
}
