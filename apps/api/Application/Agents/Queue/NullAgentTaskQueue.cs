using System;
using System.Threading;
using System.Threading.Tasks;

namespace Appostolic.Api.Application.Agents.Queue;

public sealed class NullAgentTaskQueue : IAgentTaskQueue
{
    public Task EnqueueAsync(Guid taskId, CancellationToken ct = default)
    {
        // No-op queue for Development until a real processor is wired up.
        return Task.CompletedTask;
    }
}
