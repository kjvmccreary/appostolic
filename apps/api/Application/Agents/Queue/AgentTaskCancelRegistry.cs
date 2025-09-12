using System.Collections.Concurrent;

namespace Appostolic.Api.Application.Agents.Queue;

/// <summary>
/// Process-local registry for cooperative task cancellation.
/// Worker/orchestrator checks this to short-circuit Running tasks.
/// </summary>
public sealed class AgentTaskCancelRegistry
{
    private readonly ConcurrentDictionary<Guid, DateTime> _requested = new();

    public void RequestCancel(Guid taskId)
        => _requested[taskId] = DateTime.UtcNow;

    public bool IsCancelRequested(Guid taskId)
        => _requested.ContainsKey(taskId);

    public bool TryClear(Guid taskId)
        => _requested.TryRemove(taskId, out _);
}
