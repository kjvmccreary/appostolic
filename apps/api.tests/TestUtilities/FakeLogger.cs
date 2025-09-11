using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Tests.TestUtilities;

public sealed class FakeLogger<T> : ILogger<T>, IDisposable
{
    private readonly ConcurrentQueue<(LogLevel level, EventId eventId, string message, Exception? exception)> _logs = new();

    public IReadOnlyCollection<(LogLevel level, EventId eventId, string message, Exception? exception)> Logs => _logs.ToList();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => this;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        _logs.Enqueue((logLevel, eventId, msg, exception));
    }

    public void Dispose() { }
}
