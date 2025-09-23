using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Appostolic.Api.Infrastructure.Auth.Jwt;

namespace Appostolic.Api.Tests.Auth;

public class DualKeySigningTests
{
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);
    private static byte[] Key32()
    {
        var b = new byte[32];
        Random.Shared.NextBytes(b);
        return b;
    }

    [Fact]
    public void Token_Issued_With_First_Key_Validates_After_Second_Added()
    {
        // Arrange initial single key options
    var keyA = Key32();
    var keyB = Key32();
        var services = new ServiceCollection();
        services.Configure<AuthJwtOptions>(o =>
        {
            o.SigningKeysBase64Csv = B64(keyA); // single key list
            o.Issuer = "test-iss";
            o.Audience = "test-aud";
        });
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<IJwtTokenService>();

        var tokenIssuedWithA = svc.IssueNeutralToken("user1", 1);
        var handler = new JwtSecurityTokenHandler();

        // Act 1: validate token with first key only
        handler.ValidateToken(tokenIssuedWithA, svc.CreateValidationParameters(), out _);

        // Rebuild provider with both keys (rotation overlap)
        services = new ServiceCollection();
        services.Configure<AuthJwtOptions>(o =>
        {
            o.SigningKeysBase64Csv = string.Join(',', B64(keyA), B64(keyB));
            o.Issuer = "test-iss";
            o.Audience = "test-aud";
        });
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        provider = services.BuildServiceProvider();
        svc = provider.GetRequiredService<IJwtTokenService>();

        // Act 2: validate legacy token (signed with A) under dual-key config
        handler.ValidateToken(tokenIssuedWithA, svc.CreateValidationParameters(), out _);

        // Issue new token with active (still first in list = A) and validate
        var tokenStillA = svc.IssueNeutralToken("user1", 1);
        handler.ValidateToken(tokenStillA, svc.CreateValidationParameters(), out _);

        // Simulate cutover: reorder keys so B becomes active signer
        services = new ServiceCollection();
        services.Configure<AuthJwtOptions>(o =>
        {
            o.SigningKeysBase64Csv = string.Join(',', B64(keyB)); // only B now
            o.Issuer = "test-iss";
            o.Audience = "test-aud";
        });
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        provider = services.BuildServiceProvider();
        svc = provider.GetRequiredService<IJwtTokenService>();

        // Legacy token (A) should now fail because A removed (expected behavior after grace) while newly issued with B validates.
        Assert.ThrowsAny<Exception>(() => handler.ValidateToken(tokenIssuedWithA, svc.CreateValidationParameters(), out _));
        var tokenWithB = svc.IssueNeutralToken("user1", 1);
        handler.ValidateToken(tokenWithB, svc.CreateValidationParameters(), out _);
    }

    [Fact]
    public void VerifyAllSigningKeys_ReturnsTrue_For_Valid_Config()
    {
        var services = new ServiceCollection();
        services.Configure<AuthJwtOptions>(o =>
        {
            o.SigningKeysBase64Csv = string.Join(',', B64(Key32()), B64(Key32()));
            o.Issuer = "test-iss";
            o.Audience = "test-aud";
        });
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<IJwtTokenService>();
        Assert.True(svc.VerifyAllSigningKeys());
    }
}
