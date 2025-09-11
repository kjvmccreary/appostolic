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

    // Minimal navs (optional): can be added later if needed

    public AgentTask() { }

    public AgentTask(Guid id, Guid agentId, string inputJson)
    {
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId is required", nameof(agentId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        AgentId = agentId;
        InputJson = inputJson;
        CreatedAt = DateTime.UtcNow;
    }
}
