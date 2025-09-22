using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
}

public class JwtTokenService : IJwtTokenService
{
    private readonly AuthJwtOptions _opts;
    private readonly SigningCredentials _signingCreds;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenService(IOptions<AuthJwtOptions> options)
    {
        _opts = options.Value;
        var keyBytes = _opts.GetSigningKeyBytes();
        _signingCreds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
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

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(_opts.AccessTtlMinutes),
            signingCredentials: _signingCreds
        );
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
        IssuerSigningKey = new SymmetricSecurityKey(_opts.GetSigningKeyBytes()),
        ClockSkew = TimeSpan.FromSeconds(_opts.ClockSkewSeconds)
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
        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(_opts.AccessTtlMinutes),
            signingCredentials: _signingCreds
        );
        return _handler.WriteToken(token);
    }

    private static long Epoch(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeSeconds();
}
