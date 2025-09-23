using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Infrastructure.Auth.Refresh;

/// <summary>
/// Provides an in-memory sliding window rate limiter for refresh endpoint abuse protection.
/// Not distributed; suitable for single-instance or best-effort multi-instance (will under-enforce when scaled out).
/// </summary>
public interface IRefreshRateLimiter
{
    /// <summary>
    /// Evaluates whether the caller identified by (userId, ip) should be limited.
    /// Returns an Evaluation containing block decision and remaining attempts (approximate).
    /// </summary>
    RateLimitEvaluation Evaluate(Guid? userId, string ip);
}

public record RateLimitEvaluation(bool IsLimited, bool DryRun, int Attempts, int Max, int Remaining, int WindowSeconds);

public class InMemoryRefreshRateLimiter : IRefreshRateLimiter
{
    private readonly RefreshRateLimitOptions _opts;
    private readonly ConcurrentDictionary<string, SlidingWindowCounter> _counters = new();

    public InMemoryRefreshRateLimiter(IOptions<RefreshRateLimitOptions> opts)
    {
        _opts = opts.Value;
    }

    public RateLimitEvaluation Evaluate(Guid? userId, string ip)
    {
        var key = BuildKey(userId, ip);
        var now = DateTime.UtcNow;
        var counter = _counters.GetOrAdd(key, _ => new SlidingWindowCounter(now));
        var attempts = counter.Increment(now, _opts.WindowSeconds);
        var limited = attempts > _opts.Max && !_opts.DryRun;
        var remaining = Math.Max(0, _opts.Max - attempts);
        return new RateLimitEvaluation(limited, _opts.DryRun, attempts, _opts.Max, remaining, _opts.WindowSeconds);
    }

    private static string BuildKey(Guid? userId, string ip)
        => userId.HasValue && userId != Guid.Empty ? $"u:{userId}:{ip}" : $"ip:{ip}";

    private sealed class SlidingWindowCounter
    {
        private readonly object _lock = new();
        private DateTime _windowStart;
        private int _count;

        public SlidingWindowCounter(DateTime start)
        {
            _windowStart = start;
        }

        public int Increment(DateTime now, int windowSeconds)
        {
            lock (_lock)
            {
                if ((now - _windowStart).TotalSeconds >= windowSeconds)
                {
                    // reset window
                    _windowStart = now;
                    _count = 0;
                }
                _count++;
                return _count;
            }
        }
    }
}
