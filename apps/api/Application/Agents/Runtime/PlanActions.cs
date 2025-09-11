using System.Text.Json;

namespace Appostolic.Api.Application.Agents.Runtime;

public abstract record PlanAction;
public sealed record UseTool(string Name, JsonDocument Input) : PlanAction;
public sealed record FinalAnswer(JsonDocument Result) : PlanAction;
