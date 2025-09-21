using System;
using System.Security.Cryptography;
using System.Text;

namespace Appostolic.Api.Infrastructure.Auth.Jwt;

/// <summary>
/// Central helper for refresh token hashing (Base64(SHA256(UTF8(token)))).
/// Consolidates previously duplicated inline logic to prevent divergence.
/// </summary>
public static class RefreshTokenHashing
{
    public static string Hash(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token cannot be null or whitespace", nameof(token));
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }
}
