using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Appostolic.Api.Application.Auth; // Metrics instrumentation

namespace Appostolic.Api.Infrastructure.Auth.Jwt;

/// <summary>
/// Issues and validates access JWTs (neutral user context for Story 1). Later stories will enrich with tenant / roles.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Issue a neutral (no tenant) access token. Includes user token version claim (v) for revocation.
    /// </summary>
    string IssueNeutralToken(string subject, int tokenVersion, string? email = null);
    /// <summary>
    /// Issue a neutral token with additional custom claims (test-only extension overload).
    /// </summary>
    string IssueNeutralToken(string subject, int tokenVersion, string? email, IEnumerable<Claim> extraClaims);

    /// <summary>
    /// Build TokenValidationParameters based on current options.
    /// </summary>
    TokenValidationParameters CreateValidationParameters();

    /// <summary>
    /// Issue a tenant-scoped access token including tenant claims + roles bitmask and token version claim (v).
    /// </summary>
    string IssueTenantToken(string subject, Guid tenantId, string tenantSlug, int rolesBitmask, int tokenVersion, string? email = null);
    /// <summary>
    /// Issue a tenant token with additional custom claims (test-only extension overload).
    /// </summary>
    string IssueTenantToken(string subject, Guid tenantId, string tenantSlug, int rolesBitmask, int tokenVersion, string? email, IEnumerable<Claim> extraClaims);

    /// <summary>
    /// Issues a short-lived token and validates it against all configured verification keys to ensure rotation safety. Returns true if validation succeeds across all keys.
    /// </summary>
    bool VerifyAllSigningKeys();
}

public class JwtTokenService : IJwtTokenService
{
    private readonly AuthJwtOptions _opts;
    private readonly SigningCredentials _signingCreds;
    private readonly List<SecurityKey> _allVerificationKeys;
    private readonly string _activeKeyId;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(IOptions<AuthJwtOptions> options)
    {
        _opts = options.Value;
        var keyBytesList = _opts.GetSigningKeyBytesList();
        // Derive a simple kid from first 8 bytes hex of each key for identification.
        _allVerificationKeys = new List<SecurityKey>();
        for (int i = 0; i < keyBytesList.Count; i++)
        {
            var b = keyBytesList[i];
            var sk = new SymmetricSecurityKey(b);
            var sliceLen = Math.Min(8, b.Length);
            var slice = new byte[sliceLen];
            Array.Copy(b, slice, sliceLen);
            var kid = BitConverter.ToString(slice).Replace("-", string.Empty).ToLowerInvariant();
            sk.KeyId = kid;
            _allVerificationKeys.Add(sk);
        }
        var active = (SymmetricSecurityKey)_allVerificationKeys[0];
        _activeKeyId = active.KeyId!;
        _signingCreds = new SigningCredentials(active, SecurityAlgorithms.HmacSha256);
    }

    public string IssueNeutralToken(string subject, int tokenVersion, string? email = null)
    {
        return IssueNeutralToken(subject, tokenVersion, email, Array.Empty<Claim>());
    }

    public string IssueNeutralToken(string subject, int tokenVersion, string? email, IEnumerable<Claim> extraClaims)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, Epoch(now).ToString(), ClaimValueTypes.Integer64),
            new("v", tokenVersion.ToString())
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            // Add both the registered 'email' claim and the ClaimTypes.Email variant so downstream code
            // that relies on either form (e.g., user.FindFirstValue("email") or ClaimTypes.Email) succeeds.
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var header = new JwtHeader(_signingCreds);
        header[JwtHeaderParameterNames.Kid] = _activeKeyId;
        var payload = new JwtPayload(_opts.Issuer, _opts.Audience, claims, now.AddSeconds(-5), now.AddMinutes(_opts.AccessTtlMinutes));
        var token = new JwtSecurityToken(header, payload);
        // Story 4 metrics: record active key usage.
        AuthMetrics.IncrementKeyRotationTokenSigned(_activeKeyId);
        return _handler.WriteToken(token);
    }

    public TokenValidationParameters CreateValidationParameters() => new()
    {
        ValidIssuer = _opts.Issuer,
        ValidAudience = _opts.Audience,
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(_opts.ClockSkewSeconds),
        // Allow validation against any configured key.
        IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
        {
            if (!string.IsNullOrWhiteSpace(kid))
            {
                var match = _allVerificationKeys.Find(k => k.KeyId == kid);
                if (match != null) return new[] { match };
            }
            return _allVerificationKeys; // fallback: attempt all
        }
    };

    public string IssueTenantToken(string subject, Guid tenantId, string tenantSlug, int rolesBitmask, int tokenVersion, string? email = null)
    {
        return IssueTenantToken(subject, tenantId, tenantSlug, rolesBitmask, tokenVersion, email, Array.Empty<Claim>());
    }

    public string IssueTenantToken(string subject, Guid tenantId, string tenantSlug, int rolesBitmask, int tokenVersion, string? email, IEnumerable<Claim> extraClaims)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, Epoch(now).ToString(), ClaimValueTypes.Integer64),
            new("tenant_id", tenantId.ToString()),
            new("tenant_slug", tenantSlug),
            new("roles", rolesBitmask.ToString()),
            new("v", tokenVersion.ToString())
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            // Mirror neutral token behavior: emit both forms of email claim.
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
            claims.Add(new Claim(ClaimTypes.Email, email));
        }
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }
        var header = new JwtHeader(_signingCreds);
        header[JwtHeaderParameterNames.Kid] = _activeKeyId;
        var payload = new JwtPayload(_opts.Issuer, _opts.Audience, claims, now.AddSeconds(-5), now.AddMinutes(_opts.AccessTtlMinutes));
        var token = new JwtSecurityToken(header, payload);
        // Story 4 metrics: record active key usage.
        AuthMetrics.IncrementKeyRotationTokenSigned(_activeKeyId);
        return _handler.WriteToken(token);
    }

    private static long Epoch(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeSeconds();

    public bool VerifyAllSigningKeys()
    {
        // Story 4: Avoid inflating key usage metrics with probe token; construct manually & validate.
        string? probeToken;
        try
        {
            var now = DateTime.UtcNow;
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, "health_probe"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.Iat, Epoch(now).ToString(), ClaimValueTypes.Integer64),
                new("v", "0")
            };
            var header = new JwtHeader(_signingCreds);
            header[JwtHeaderParameterNames.Kid] = _activeKeyId;
            var payload = new JwtPayload(_opts.Issuer, _opts.Audience, claims, now.AddSeconds(-5), now.AddMinutes(5));
            var token = new JwtSecurityToken(header, payload);
            probeToken = _handler.WriteToken(token);
        }
        catch
        {
            AuthMetrics.IncrementKeyRotationValidationFailure("issue");
            return false;
        }

        try
        {
            var parameters = CreateValidationParameters();
            _handler.ValidateToken(probeToken, parameters, out _);
            return true;
        }
        catch
        {
            AuthMetrics.IncrementKeyRotationValidationFailure("validate");
            return false;
        }
    }
}
