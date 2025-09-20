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
    /// Issue an access token representing a neutral user identity (for smoke testing baseline infrastructure).
    /// </summary>
    string IssueNeutralToken(string subject, string? email = null);

    /// <summary>
    /// Build TokenValidationParameters based on current options.
    /// </summary>
    TokenValidationParameters CreateValidationParameters();
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

    public string IssueNeutralToken(string subject, string? email = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, Epoch(now).ToString(), ClaimValueTypes.Integer64)
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
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

    private static long Epoch(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeSeconds();
}
