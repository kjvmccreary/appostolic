using System;
using System.Collections.Generic;
using System.Text.Json;
using Appostolic.Api.Application.Guardrails;

namespace Appostolic.Api.Application.Agents.Api;

// Requests
/// <summary>
/// Optional guardrail context supplied when creating an agent task. Allows callers to provide
/// precomputed signals, summaries, and policy hints before the task is enqueued.
/// </summary>
public sealed record AgentTaskGuardrailContext(
    IReadOnlyList<string>? Signals,
    string? PromptSummary,
    string? Channel,
    string? PolicyKey,
    Guid? UserId,
    IReadOnlyList<string>? PresetIds
);

public record CreateAgentTaskRequest(Guid AgentId, JsonDocument Input, AgentTaskGuardrailContext? Guardrails);

// Summaries and details for listings and fetch-by-id
public record AgentTaskSummary(
    Guid Id,
    Guid AgentId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int TotalTokens,
    GuardrailDecision? GuardrailDecision
);

public record AgentTaskDetails(
    Guid Id,
    Guid AgentId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int TotalPromptTokens,
    int TotalCompletionTokens,
    int TotalTokens,
    decimal? EstimatedCostUsd,
    JsonDocument? Result,
    string? ErrorMessage,
    GuardrailDecision? GuardrailDecision,
    JsonDocument? GuardrailMetadata
);

public record AgentTraceDto(
    Guid Id,
    int StepNumber,
    string Kind,
    string Name,
    int DurationMs,
    int PromptTokens,
    int CompletionTokens,
    string? Error,
    JsonDocument Input,
    JsonDocument? Output,
    DateTime CreatedAt
);
