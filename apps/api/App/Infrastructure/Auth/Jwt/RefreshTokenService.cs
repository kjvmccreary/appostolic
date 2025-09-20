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
    /// Issue a neutral refresh token for a user, persisting hash. Returns plaintext token and expiry.
    /// </summary>
    Task<(string token, DateTime expiresAt)> IssueNeutralAsync(Guid userId, int ttlDays);

    /// <summary>
    /// Validate a presented neutral refresh token (plaintext). Returns entity if active and unexpired; null otherwise.
    /// </summary>
    Task<RefreshToken?> ValidateNeutralAsync(Guid userId, string plaintextToken);

    /// <summary>
    /// Revoke a refresh token (idempotent) by setting RevokedAt if not already set.
    /// </summary>
    Task RevokeAsync(Guid refreshTokenId);
}

public class RefreshTokenService : IRefreshTokenService
{
    private readonly AppDbContext _db;

    public RefreshTokenService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<(string token, DateTime expiresAt)> IssueNeutralAsync(Guid userId, int ttlDays)
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
            ExpiresAt = expires
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();
        return (token, expires);
    }

    public async Task<RefreshToken?> ValidateNeutralAsync(Guid userId, string plaintextToken)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken)) return null;
        var hash = Hash(plaintextToken);
        var now = DateTime.UtcNow;
        var rt = await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.TokenHash == hash && r.Purpose == "neutral");
        if (rt == null) return null;
        if (rt.RevokedAt.HasValue) return null;
        if (rt.ExpiresAt <= now) return null;
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
