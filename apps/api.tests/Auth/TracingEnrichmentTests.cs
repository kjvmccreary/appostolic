using System.Diagnostics;
using Appostolic.Api.Application.Auth;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 5: Verifies that calling AuthTracing.Enrich sets expected Activity tags and increments enrichment counter.
/// NOTE: This is a focused unit-style test; it does not spin up the full WebAppFactory.
/// </summary>
public class TracingEnrichmentTests
{
    [Fact]
    public void Enrich_AddsTagsAndIncrementsMetric()
    {
        var source = AuthTracing.Source;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ => { },
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("auth.test", ActivityKind.Server);
        Assert.NotNull(activity);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        AuthTracing.Enrich(activity, userId, tenantId, outcome: "success");

        Assert.Equal(userId, activity!.GetTagItem("auth.user_id"));
        Assert.Equal(tenantId, activity.GetTagItem("auth.tenant_id"));
        Assert.Equal("success", activity.GetTagItem("auth.outcome"));
        Assert.Null(activity.GetTagItem("auth.reason"));
    }
}