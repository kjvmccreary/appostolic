using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Application.Privacy;

/// <summary>
/// Provides stable, deterministic hashing for select PII values (email, phone) using SHA-256 + optional pepper.
/// Hashes are suitable only for identification / correlation (NOT for password storage).
/// </summary>
public interface IPIIHasher
{
    string HashEmail(string? email);
    string HashPhone(string? phone);
}

internal sealed class Sha256PIIHasher : IPIIHasher
{
    private readonly string _pepper;

    public Sha256PIIHasher(IOptions<PrivacyOptions> opts)
    {
        _pepper = opts.Value.PIIHashPepper ?? string.Empty;
    }

    public string HashEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        // Normalize: lowercase + trim
        var norm = email.Trim().ToLowerInvariant();
        return Compute(norm);
    }

    public string HashPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        // Basic normalization: strip non-digits, keep leading + if present; future: libphonenumber
        var trimmed = phone.Trim();
        var plus = trimmed.StartsWith("+");
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return string.Empty;
        var norm = plus ? "+" + digits : digits; // naive E.164-ish normalization
        return Compute(norm);
    }

    private string Compute(string normalized)
    {
        // Format: hex lowercase sha256 of normalized + ':' + pepper
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized + ":" + _pepper);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
