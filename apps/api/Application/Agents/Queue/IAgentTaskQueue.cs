using System;
using System.Threading;
using System.Threading.Tasks;

namespace Appostolic.Api.Application.Agents.Queue;

public interface IAgentTaskQueue
{
    Task EnqueueAsync(Guid taskId, CancellationToken ct = default);
}
