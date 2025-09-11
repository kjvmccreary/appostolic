using System.Threading.Channels;

namespace Appostolic.Api.Application.Agents.Queue;

/// <summary>
/// In-memory, process-local queue for development. Deterministic ordering and backpressure via Channel.
/// </summary>
public sealed class InMemoryAgentTaskQueue : IAgentTaskQueue
{
    private readonly Channel<Guid> _channel;

    public InMemoryAgentTaskQueue()
    {
        // Unbounded with single-reader, multi-writer semantics; allow sync continuations for low overhead.
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        };
        _channel = Channel.CreateUnbounded<Guid>(options);
    }

    internal ChannelReader<Guid> Reader => _channel.Reader;

    public async Task EnqueueAsync(Guid taskId, CancellationToken ct = default)
    {
        // Respect backpressure if the channel temporarily cannot accept writes.
        while (await _channel.Writer.WaitToWriteAsync(ct).ConfigureAwait(false))
        {
            if (_channel.Writer.TryWrite(taskId)) return;
        }
    }
}
