using System;

namespace Appostolic.Api.Infrastructure.Auth.Refresh;

/// <summary>
/// Story 14: Emergency revert no-op refresh rate limiter (always unlimited).
/// </summary>
public sealed class NoopRefreshRateLimiter : IRefreshRateLimiter
{
    public RateLimitEvaluation Evaluate(Guid? userId, string ip) => new RateLimitEvaluation(
        IsLimited: false,
        DryRun: false,
        Attempts: 0,
        Max: int.MaxValue,
        Remaining: int.MaxValue,
        WindowSeconds: 0
    );
}
