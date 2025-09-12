using System;
using System.Text.Json;

namespace Appostolic.Api.Application.Agents.Api;

// Requests
public record CreateAgentTaskRequest(Guid AgentId, JsonDocument Input);

// Summaries and details for listings and fetch-by-id
public record AgentTaskSummary(
    Guid Id,
    Guid AgentId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    int TotalTokens
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
    string? ErrorMessage
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
