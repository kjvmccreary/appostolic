using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.Infrastructure.Auth.Jwt;

/// <summary>
/// Issues and persists hashed refresh tokens. Story 2 scope: neutral purpose only.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>
    /// Issue a neutral refresh token for a user, persisting hash. Returns refresh token Id, plaintext token and expiry.
    /// </summary>
    Task<(Guid id, string token, DateTime expiresAt)> IssueNeutralAsync(Guid userId, int ttlDays, string? fingerprint = null);

    /// <summary>
    /// Validate a presented neutral refresh token (plaintext). Returns entity if active and unexpired; null otherwise.
    /// </summary>
    Task<RefreshToken?> ValidateNeutralAsync(Guid userId, string plaintextToken);

    /// <summary>
    /// Revoke a refresh token (idempotent) by setting RevokedAt if not already set.
    /// </summary>
    Task RevokeAsync(Guid refreshTokenId);

    /// <summary>
    /// Bulk revoke all active neutral refresh tokens for a user (idempotent). Returns count affected.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId);
}

public class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _db;

    private readonly Microsoft.Extensions.Options.IOptions<Appostolic.Api.Application.Auth.SlidingRefreshOptions>? _slidingOpts;

    public RefreshTokenService(AppDbContext db, Microsoft.Extensions.Options.IOptions<Appostolic.Api.Application.Auth.SlidingRefreshOptions>? slidingOpts = null)
    {
        _db = db;
        _slidingOpts = slidingOpts; // optional for backwards compat in tests
    }

    public async Task<(Guid id, string token, DateTime expiresAt)> IssueNeutralAsync(Guid userId, int ttlDays, string? fingerprint = null)
    {
        var token = GenerateToken();
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        var expires = now.AddDays(ttlDays);

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            Purpose = "neutral",
            CreatedAt = now,
            OriginalCreatedAt = now,
            ExpiresAt = expires,
            Fingerprint = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();
        return (entity.Id, token, expires);
    }

    public async Task<RefreshToken?> ValidateNeutralAsync(Guid userId, string plaintextToken)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return null;
        var hash = Hash(plaintextToken);
        var now = DateTime.UtcNow;
        var rt = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.UserId == userId && r.TokenHash == hash && r.Purpose == "neutral");
        if (rt == null) return null;
        if (rt.RevokedAt.HasValue) return null;
        if (rt.ExpiresAt <= now) return null;
        // Update LastUsedAt (only on successful validation) then detach clone for return to avoid exposing tracking modifications upstream unintentionally.
        rt.LastUsedAt = now;
        await _db.SaveChangesAsync();
        return rt;
    }

    public async Task RevokeAsync(Guid refreshTokenId)
    {
        var rt = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Id == refreshTokenId);
        if (rt == null) return; // already gone
        if (!rt.RevokedAt.HasValue)
        {
            rt.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var tokens = await _db.RefreshTokens
            .Where(r => r.UserId == userId && r.Purpose == "neutral" && !r.RevokedAt.HasValue)
            .ToListAsync();
        foreach (var t in tokens)
        {
            t.RevokedAt = now;
        }
        if (tokens.Count > 0)
        {
            await _db.SaveChangesAsync();
        }
        return tokens.Count;
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32]; // 256-bit random
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    private static string Hash(string token)
    {
        // SHA256 over UTF8 bytes; output base64
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes)
    {
        var s = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return s;
    }
}
