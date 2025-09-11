using System.Text.Json;

namespace Appostolic.Api.Application.Agents.Model;

public record ModelPrompt(string SystemPrompt, JsonDocument Context);
public record ModelDecision(Appostolic.Api.Application.Agents.Runtime.PlanAction Action, int PromptTokens, int CompletionTokens, string Rationale);
