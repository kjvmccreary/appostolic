using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Appostolic.Api.Application.Guardrails;
using Appostolic.Api.Domain.Guardrails;

namespace Appostolic.Api.App.Endpoints;

/// <summary>
/// Exposes guardrail evaluation endpoints that merge policy layers and return preflight decisions.
/// </summary>
public static class GuardrailEndpoints
{
    public static IEndpointRouteBuilder MapGuardrailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardrails")
            .RequireAuthorization()
            .WithTags("Guardrails");

        group.MapPost("preflight", HandlePreflight)
            .WithSummary("Evaluate guardrail policies for the current tenant and user");

        return app;
    }

    private static async Task<IResult> HandlePreflight(
        ClaimsPrincipal principal,
        GuardrailPreflightRequestDto dto,
        IGuardrailEvaluator evaluator,
        IGuardrailSecurityEventWriter securityEvents,
        CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var tenantId))
        {
            return Results.BadRequest(new { error = "missing_tenant_scope" });
        }
        var userIdClaim = principal.FindFirst("sub")?.Value
                           ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var authenticatedUserId))
        {
            return Results.Unauthorized();
        }

        var targetUserId = dto.UserId ?? authenticatedUserId;
        if (dto.UserId.HasValue && dto.UserId.Value != authenticatedUserId)
        {
            return Results.Forbid();
        }

        var policyKey = string.IsNullOrWhiteSpace(dto.PolicyKey) ? "default" : dto.PolicyKey.Trim().ToLowerInvariant();
        var signals = (dto.Signals ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        var context = new GuardrailEvaluationContext
        {
            TenantId = tenantId,
            UserId = targetUserId,
            PolicyKey = policyKey,
            Signals = signals,
            Channel = dto.Channel,
            PromptSummary = dto.PromptSummary,
            PresetIds = dto.PresetIds
        };

        var result = await evaluator.EvaluateAsync(context, ct);

        if (result.Decision is GuardrailDecision.Deny or GuardrailDecision.Escalate)
        {
            GuardrailResponseFactory.EmitSecurityEvent(securityEvents, tenantId, targetUserId, dto.Channel, dto.PromptSummary, result);
        }

        var response = GuardrailResponseFactory.CreateResponse(result);

        return Results.Ok(response);
    }
}

/// <summary>
/// Request body for guardrail preflight evaluations.
/// </summary>
public sealed record GuardrailPreflightRequestDto
{
    public string? PromptSummary { get; init; }
    public List<string>? Signals { get; init; }
    public string? Channel { get; init; }
    public string? PolicyKey { get; init; }
    public List<string>? PresetIds { get; init; }
    public Guid? UserId { get; init; }
}

/// <summary>
/// Response body for guardrail preflight evaluations.
/// </summary>
public sealed record GuardrailPreflightResponseDto(
    GuardrailDecision Decision,
    string ReasonCode,
    IReadOnlyList<string> MatchedSignals,
    GuardrailPolicySnapshotDto Policy,
    IReadOnlyList<GuardrailMatchDto> Matches,
    IReadOnlyList<GuardrailPolicyTraceEntryDto> Trace
);

/// <summary>
/// Snapshot of the merged allow/deny/escalate sets returned to clients.
/// </summary>
public sealed record GuardrailPolicySnapshotDto(
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Deny,
    IReadOnlyList<string> Escalate
);

/// <summary>
/// Describes a rule match that contributed to the final decision.
/// </summary>
public sealed record GuardrailMatchDto(
    string Rule,
    string RuleType,
    string Source,
    string SourceId,
    string? Layer
);

/// <summary>
/// Trace entry describing which rules a policy layer contributed during merge.
/// </summary>
public sealed record GuardrailPolicyTraceEntryDto(
    string Source,
    string SourceId,
    string? Layer,
    IReadOnlyList<string> AddedAllow,
    IReadOnlyList<string> AddedDeny,
    IReadOnlyList<string> AddedEscalate
);
