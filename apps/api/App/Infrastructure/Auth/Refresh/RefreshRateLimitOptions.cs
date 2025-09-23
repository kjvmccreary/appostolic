using System;

namespace Appostolic.Api.Infrastructure.Auth.Refresh;

/// <summary>
/// Configuration for refresh token rate limiting (Story 3). Bound from AUTH__REFRESH_RATE_LIMIT_* variables.
/// </summary>
public class RefreshRateLimitOptions
{
    /// <summary>Sliding window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60; // AUTH__REFRESH_RATE_LIMIT_WINDOW_SECONDS
    /// <summary>Maximum allowed refresh attempts per key (user+ip or ip only) in the window.</summary>
    public int Max { get; set; } = 20; // AUTH__REFRESH_RATE_LIMIT_MAX
    /// <summary>Dry-run mode records metrics but does not block.</summary>
    public bool DryRun { get; set; } = true; // AUTH__REFRESH_RATE_LIMIT_DRY_RUN
}
