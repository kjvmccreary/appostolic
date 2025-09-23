using System;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Appostolic.Api.Infrastructure.Auth.Jwt;
using Appostolic.Api.Application.Auth;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Tests for Story 4 key rotation metrics: tokens_signed and validation_failure.
/// </summary>
public class KeyRotationMetricsTests
{
    private static string B64(byte[] b) => Convert.ToBase64String(b);
    private static byte[] Key32()
    {
        var k = new byte[32];
        Random.Shared.NextBytes(k);
        return k;
    }

    [Fact]
    public void TokensSigned_Increments_With_Issuance()
    {
        // Arrange
        var keyA = Key32();
        using var listener = new MeterListener();
        long signedCount = 0;
        listener.InstrumentPublished = (inst, was) =>
        {
            if (inst.Meter.Name == "Appostolic.Auth") listener.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
        {
            if (inst.Name == "auth.jwt.key_rotation.tokens_signed") signedCount += value;
        });
        listener.Start();

        var services = new ServiceCollection();
        services.Configure<AuthJwtOptions>(o =>
        {
            o.SigningKeysBase64Csv = B64(keyA);
            o.Issuer = "test-iss";
            o.Audience = "test-aud";
        });
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        var provider = services.BuildServiceProvider();
        var svc = provider.GetRequiredService<IJwtTokenService>();

        // Act
        for (int i = 0; i < 3; i++) _ = svc.IssueNeutralToken("user" + i, 1);
        listener.RecordObservableInstruments();
        listener.Dispose();

        // Assert
        Assert.Equal(3, signedCount);
    }

    // NOTE: Negative-path validation_failure counter test omitted. Current implementation performs issuance+validation atomically
    // using the same options snapshot; inducing a failure deterministically would require a DI seam or deliberate invalid configuration
    // injection between phases. Consider adding an IJwtProbeStrategy to enable simulation in future.
}
