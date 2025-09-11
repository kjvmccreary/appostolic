using Appostolic.Api.Domain.Agents;

namespace Appostolic.Api.Application.Agents.Runtime;

public interface IAgentOrchestrator
{
    Task RunAsync(Agent agent, AgentTask task, string tenant, string user, CancellationToken ct);
}
