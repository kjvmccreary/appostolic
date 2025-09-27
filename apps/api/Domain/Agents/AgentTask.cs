using Appostolic.Api.Application.Guardrails;

namespace Appostolic.Api.Domain.Agents;

public record class AgentTask
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    private string _inputJson = string.Empty;
    public string InputJson
    {
        get => _inputJson;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("InputJson is required", nameof(InputJson));
            _inputJson = value;
        }
    }

    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public string? ResultJson { get; set; }
    public string? ErrorMessage { get; set; }

    public GuardrailDecision? GuardrailDecision { get; set; }
    public string? GuardrailMetadataJson { get; set; }

    // Request context (tenant/user) to run the task under the same principal as the request
    public string? RequestTenant { get; init; }
    public string? RequestUser { get; init; }

    // Roll-up telemetry
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public int TotalTokens => Math.Max(0, TotalPromptTokens) + Math.Max(0, TotalCompletionTokens);
    public decimal? EstimatedCostUsd { get; set; }

    // Minimal navs (optional): can be added later if needed

    public AgentTask() { }

    public AgentTask(Guid id, Guid agentId, string inputJson)
    {
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId is required", nameof(agentId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        AgentId = agentId;
        InputJson = Application.Validation.Guard.NotNullOrWhiteSpace(inputJson, nameof(inputJson));
        CreatedAt = DateTime.UtcNow;
    }
}
