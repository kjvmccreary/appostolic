using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Appostolic.Api.Application.Auth; // Metrics namespace
using Xunit;

namespace Appostolic.Api.Tests.Auth;

public class AuthMetricsTests
{
    [Fact]
    public void PlaintextCountersIncrement()
    {
        // Arrange
        using var listener = new MeterListener();
        long emitted = 0; long suppressed = 0; long rotations = 0; long reuseDenied = 0; long expired = 0;
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
                case "auth.refresh.plaintext_suppressed": suppressed += value; break;
                case "auth.refresh.rotations": rotations += value; break;
                case "auth.refresh.reuse_denied": reuseDenied += value; break;
                case "auth.refresh.expired": expired += value; break;
            }
        });
        listener.Start();

        var userId = System.Guid.NewGuid();
        AuthMetrics.IncrementPlaintextEmitted(userId);
        AuthMetrics.IncrementPlaintextSuppressed(userId);
        AuthMetrics.IncrementRotation(userId, System.Guid.NewGuid(), System.Guid.NewGuid());
        AuthMetrics.IncrementReuseDenied(userId, System.Guid.NewGuid());
        AuthMetrics.IncrementExpired(userId, System.Guid.NewGuid());

        // Act
        listener.RecordObservableInstruments();
        listener.Dispose();

        // Assert
        Assert.Equal(1, emitted);
        Assert.Equal(1, suppressed);
        Assert.Equal(1, rotations);
        Assert.Equal(1, reuseDenied);
        Assert.Equal(1, expired);
    }
}
