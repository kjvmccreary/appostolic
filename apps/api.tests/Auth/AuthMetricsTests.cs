using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Appostolic.Api.Application.Auth; // Metrics namespace
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class AuthMetricsTests
{
    [Fact]
    public void PlaintextEmittedCounterIncrement()
    {
        // Arrange
        using var listener = new MeterListener();
        long emitted = 0; long rotations = 0; long reuseDenied = 0; long expired = 0;
        listener.InstrumentPublished = (inst, wasEnabled) =>
        {
            if (inst.Meter.Name == "Appostolic.Auth")
            {
                listener.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
        {
            switch (inst.Name)
            {
                case "auth.refresh.plaintext_emitted": emitted += value; break;
                case "auth.refresh.rotations": rotations += value; break;
                case "auth.refresh.reuse_denied": reuseDenied += value; break;
                case "auth.refresh.expired": expired += value; break;
            }
        });
        listener.Start();

        var userId = System.Guid.NewGuid();
        AuthMetrics.IncrementPlaintextEmitted(userId);
        AuthMetrics.IncrementRotation(userId, System.Guid.NewGuid(), System.Guid.NewGuid());
        AuthMetrics.IncrementReuseDenied(userId, System.Guid.NewGuid());
        AuthMetrics.IncrementExpired(userId, System.Guid.NewGuid());

        // Act
        listener.RecordObservableInstruments();
        listener.Dispose();

        // Assert
        Assert.Equal(1, emitted);
        Assert.Equal(1, rotations);
        Assert.Equal(1, reuseDenied);
        Assert.Equal(1, expired);
    }

    [Fact]
    public void Story9_NewCountersAndDurations()
    {
        using var listener = new MeterListener();
        long loginSuccess = 0, loginFailure = 0, refreshSuccess = 0, refreshFailure = 0, rateLimited = 0; long rotations = 0;
        double loginDurationObserved = 0, refreshDurationObserved = 0;
        listener.InstrumentPublished = (inst, wasEnabled) =>
        {
            if (inst.Meter.Name == "Appostolic.Auth")
            {
                listener.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
        {
            switch (inst.Name)
            {
                case "auth.login.success": loginSuccess += value; break;
                case "auth.login.failure": loginFailure += value; break;
                case "auth.refresh.success": refreshSuccess += value; break;
                case "auth.refresh.failure": refreshFailure += value; break;
                case "auth.refresh.rate_limited": rateLimited += value; break;
                case "auth.refresh.rotations": rotations += value; break; // existing to ensure no regression
            }
        });
        listener.SetMeasurementEventCallback<double>((inst, value, tags, state) =>
        {
            switch (inst.Name)
            {
                case "auth.login.duration_ms": loginDurationObserved = value; break;
                case "auth.refresh.duration_ms": refreshDurationObserved = value; break;
            }
        });
        listener.Start();

        var userId = System.Guid.NewGuid();
        AuthMetrics.IncrementLoginSuccess(userId, 2);
        AuthMetrics.IncrementLoginFailure("invalid_credentials", userId);
        AuthMetrics.IncrementRefreshSuccess(userId);
        AuthMetrics.IncrementRefreshFailure("refresh_invalid");
        AuthMetrics.IncrementRefreshRateLimited();
        AuthMetrics.RecordLoginDuration(12.3, true);
        AuthMetrics.RecordRefreshDuration(7.1, false);
        AuthMetrics.IncrementRotation(userId, System.Guid.NewGuid(), System.Guid.NewGuid());

        listener.RecordObservableInstruments();
        listener.Dispose();

        Assert.Equal(1, loginSuccess);
        Assert.Equal(1, loginFailure);
        Assert.Equal(1, refreshSuccess);
        Assert.Equal(1, refreshFailure);
        Assert.Equal(1, rateLimited);
        Assert.Equal(1, rotations);
        Assert.True(loginDurationObserved > 0);
        Assert.True(refreshDurationObserved > 0);
    }
}
