using System.Text.Json;

namespace Appostolic.Api.Application.Agents.Runtime;

public sealed class AgentState
{
    public Guid TaskId { get; init; }
    public Guid AgentId { get; init; }
    public JsonDocument InputJson { get; init; } = JsonDocument.Parse("{}");

    // Mutable per step working memory. Represented as a JsonDocument holding an object; replace as needed.
    public JsonDocument Scratchpad { get; set; } = JsonDocument.Parse("{}");

    public int StepNumber { get; set; } = 1; // start at 1
    public int MaxSteps { get; init; } = 10;
    public bool IsComplete { get; set; }
    public JsonDocument? ResultJson { get; set; }
}
