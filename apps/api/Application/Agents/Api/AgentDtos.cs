namespace Appostolic.Api.Application.Agents.Api;

public record AgentUpsertRequest(
    string Name,
    string Model,
    double Temperature,
    int MaxSteps,
    string SystemPrompt,
    string[] ToolAllowlist,
    bool? IsEnabled
);

public record AgentListItem(
    Guid Id,
    string Name,
    string Model,
    double Temperature,
    int MaxSteps,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record AgentDetails(
    Guid Id,
    string Name,
    string Model,
    double Temperature,
    int MaxSteps,
    string SystemPrompt,
    string[] ToolAllowlist,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
