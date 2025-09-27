using System;
using System.Linq;
using Appostolic.Api.Application.Guardrails;

namespace Appostolic.Api.App.Endpoints;

/// <summary>
/// Shared helpers for mapping guardrail evaluation results to HTTP responses and emitting
/// structured security events with consistent truncation rules.
/// </summary>
internal static class GuardrailResponseFactory
{
    /// <summary>
    /// Converts an evaluation result into the DTO shape returned by guardrail endpoints.
    /// </summary>
    public static GuardrailPreflightResponseDto CreateResponse(GuardrailEvaluationResult result)
    {
        return new GuardrailPreflightResponseDto(
            result.Decision,
            result.ReasonCode,
            result.MatchedSignals,
            new GuardrailPolicySnapshotDto(result.Snapshot.Allow, result.Snapshot.Deny, result.Snapshot.Escalate),
            result.Matches.Select(match => new GuardrailMatchDto(
                match.Rule,
                match.RuleType,
                match.Source.ToString().ToLowerInvariant(),
                match.SourceId,
                match.Layer?.ToString().ToLowerInvariant()
            )).ToList(),
            result.Trace.Select(entry => new GuardrailPolicyTraceEntryDto(
                entry.Source.ToString().ToLowerInvariant(),
                entry.SourceId,
                entry.Layer?.ToString().ToLowerInvariant(),
                entry.AddedAllow,
                entry.AddedDeny,
                entry.AddedEscalate
            )).ToList()
        );
    }

    /// <summary>
    /// Emits a guardrail security event when a deny/escalate decision occurs.
    /// </summary>
    public static void EmitSecurityEvent(
        IGuardrailSecurityEventWriter writer,
        Guid tenantId,
        Guid userId,
        string? channel,
        string? promptSummary,
        GuardrailEvaluationResult result)
    {
        var type = result.Decision == GuardrailDecision.Deny ? "guardrail_denied" : "guardrail_escalated";
        var evt = writer.Create(type, builder =>
        {
            builder.Tenant(tenantId)
                   .User(userId)
                   .Decision(result.Decision.ToString().ToLowerInvariant())
                   .Reason("policy_match");
            if (!string.IsNullOrWhiteSpace(channel)) builder.Channel(channel);
            if (!string.IsNullOrWhiteSpace(promptSummary))
            {
                builder.Meta("prompt_preview", Truncate(promptSummary, 120));
            }
            if (result.Matches.FirstOrDefault() is GuardrailMatch match)
            {
                builder.Rule(match.RuleType, match.Rule)
                       .Source($"{match.Source.ToString().ToLowerInvariant()}:{match.SourceId}");
            }
        });
        writer.Emit(evt);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }
        return value[..maxLength];
    }
}
