namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Story 11: Sliding refresh expiration configuration.
/// If enabled (MaxLifetimeDays > 0), each successful rotation can extend the refresh token expiry up to a sliding window (SlidingWindowDays)
/// but never beyond CreatedAt + MaxLifetimeDays.
/// </summary>
public class SlidingRefreshOptions
{
    /// <summary>
    /// Number of days to extend the expiry forward on each successful rotation (window length). 0 disables sliding behavior.
    /// </summary>
    public int SlidingWindowDays { get; set; } = 0; // AUTH__REFRESH_SLIDING_WINDOW_DAYS

    /// <summary>
    /// Absolute maximum lifetime (days) from the original session creation. 0 disables max lifetime enforcement.
    /// </summary>
    public int MaxLifetimeDays { get; set; } = 0; // AUTH__REFRESH_MAX_LIFETIME_DAYS
}
