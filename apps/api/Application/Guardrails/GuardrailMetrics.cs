using System.Diagnostics.Metrics;

namespace Appostolic.Api.Application.Guardrails;

/// <summary>
/// OpenTelemetry metric instruments for the guardrail subsystem. Emits counters and histograms under the meter "Appostolic.Guardrails".
/// Provides helper methods so evaluators can increment with consistent tag vocabularies (outcome, channel, source, rule_type).
/// </summary>
public static class GuardrailMetrics
{
    public static readonly Meter Meter = new("Appostolic.Guardrails");

    public static readonly Counter<long> PreflightRequests = Meter.CreateCounter<long>(
        name: "guardrail.preflight.requests",
        unit: "{request}",
        description: "Count of guardrail preflight evaluations broken down by outcome and channel."
    );

    public static readonly Histogram<double> PreflightDurationMs = Meter.CreateHistogram<double>(
        name: "guardrail.preflight.duration_ms",
        unit: "ms",
        description: "Latency of guardrail preflight evaluations in milliseconds."
    );

    public static readonly Counter<long> RuleMatches = Meter.CreateCounter<long>(
        name: "guardrail.preflight.rule_matches",
        unit: "{match}",
        description: "Count of rule matches encountered during guardrail preflight evaluations (deny, escalate, allow)."
    );

    public static readonly Counter<long> SecurityEventsEmitted = Meter.CreateCounter<long>(
        name: "guardrail.security.events_emitted",
        unit: "{event}",
        description: "Count of structured guardrail security events emitted."
    );

    /// <summary>
    /// Records a guardrail preflight evaluation with standardized tags.
    /// </summary>
    public static void RecordPreflight(double elapsedMs, GuardrailDecision decision, string? channel)
    {
        var tags = new System.Diagnostics.TagList
        {
            { "outcome", decision.ToString().ToLowerInvariant() },
            { "channel", string.IsNullOrWhiteSpace(channel) ? "unspecified" : channel }
        };
        PreflightRequests.Add(1, tags);
        PreflightDurationMs.Record(elapsedMs, tags);
    }

    /// <summary>
    /// Records a rule match (deny/escalate/allow) tagged by rule type and source.
    /// </summary>
    public static void RecordRuleMatch(string ruleType, string source)
    {
        var tags = new System.Diagnostics.TagList
        {
            { "rule_type", ruleType },
            { "source", source }
        };
        RuleMatches.Add(1, tags);
    }

    /// <summary>
    /// Records that a guardrail security event was emitted.
    /// </summary>
    public static void RecordSecurityEvent(string type)
    {
        var tags = new System.Diagnostics.TagList { { "type", type } };
        SecurityEventsEmitted.Add(1, tags);
    }
}
