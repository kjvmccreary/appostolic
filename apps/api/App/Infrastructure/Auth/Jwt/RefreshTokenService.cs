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
