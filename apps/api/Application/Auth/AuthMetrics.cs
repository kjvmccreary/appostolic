using System.Diagnostics.Metrics;

namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Auth-related metrics instruments (Story 9 scaffold). Emitted via OpenTelemetry Meter "Appostolic.Auth".
/// NOTE: Counter names are stable public contract; avoid renaming once dashboards consume them.
/// </summary>
public static class AuthMetrics
{
    public static readonly Meter Meter = new("Appostolic.Auth");

    // Token lifecycle
    public static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>(
        name: "auth.tokens.issued",
        unit: "{token}",
        description: "Count of access tokens issued (neutral + tenant)."
    );

    public static readonly Counter<long> RefreshRotations = Meter.CreateCounter<long>(
        name: "auth.refresh.rotations",
        unit: "{rotation}",
        description: "Count of successful refresh rotations (old revoked + new issued)."
    );

    public static readonly Counter<long> RefreshReuseDenied = Meter.CreateCounter<long>(
        name: "auth.refresh.reuse_denied",
        unit: "{event}",
        description: "Count of refresh attempts denied due to reuse of an already revoked token."
    );

    public static readonly Counter<long> RefreshExpired = Meter.CreateCounter<long>(
        name: "auth.refresh.expired",
        unit: "{event}",
        description: "Count of refresh attempts denied due to expiration."
    );

    public static readonly Counter<long> RefreshPlaintextEmitted = Meter.CreateCounter<long>(
        name: "auth.refresh.plaintext_emitted",
        unit: "{event}",
        description: "TEMPORARY: Count of times a plaintext refresh token was included in JSON responses (will be removed after flag retirement)."
    );

    public static readonly Counter<long> RefreshPlaintextSuppressed = Meter.CreateCounter<long>(
        name: "auth.refresh.plaintext_suppressed",
        unit: "{event}",
        description: "Count of times plaintext refresh token emission was suppressed (flag disabled)."
    );

    public static readonly Counter<long> LogoutSingle = Meter.CreateCounter<long>(
        name: "auth.logout.single",
        unit: "{event}",
        description: "Count of single logout operations."
    );

    public static readonly Counter<long> LogoutAll = Meter.CreateCounter<long>(
        name: "auth.logout.all",
        unit: "{event}",
        description: "Count of global logout (all sessions) operations." 
    );

    // Login / Refresh outcome counters (Story 9 additions)
    public static readonly Counter<long> LoginSuccess = Meter.CreateCounter<long>(
        name: "auth.login.success",
        unit: "{event}",
        description: "Count of successful login operations (neutral token issuance)."
    );

    public static readonly Counter<long> LoginFailure = Meter.CreateCounter<long>(
        name: "auth.login.failure",
        unit: "{event}",
        description: "Count of failed login attempts (invalid credentials, unknown user, etc)." 
    );

    public static readonly Counter<long> RefreshSuccess = Meter.CreateCounter<long>(
        name: "auth.refresh.success",
        unit: "{event}",
        description: "Count of successful refresh rotations (neutral token re-issuance)." 
    );

    public static readonly Counter<long> RefreshFailure = Meter.CreateCounter<long>(
        name: "auth.refresh.failure",
        unit: "{event}",
        description: "Count of failed refresh attempts (invalid, expired, reuse, forbidden tenant, etc)." 
    );

    public static readonly Counter<long> RefreshRateLimited = Meter.CreateCounter<long>(
        name: "auth.refresh.rate_limited",
        unit: "{event}",
        description: "Count of refresh attempts rejected due to rate limiting." 
    );

    // Latency histograms (milliseconds) - low cardinality tags only (outcome)
    public static readonly Histogram<double> LoginDurationMs = Meter.CreateHistogram<double>(
        name: "auth.login.duration_ms",
        unit: "ms",
        description: "End-to-end duration for login requests (from endpoint entry to response)." 
    );

    public static readonly Histogram<double> RefreshDurationMs = Meter.CreateHistogram<double>(
        name: "auth.refresh.duration_ms",
        unit: "ms",
        description: "End-to-end duration for refresh requests (from endpoint entry to response)." 
    );


    /// <summary>
    /// Increment issued tokens counter (neutral or tenant). Pass tenantId for tenant-scoped tokens.
    /// </summary>
    public static void IncrementTokensIssued(Guid userId, Guid? tenantId = null)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId } };
        if (tenantId.HasValue) tags.Add("tenant_id", tenantId.Value);
        TokensIssued.Add(1, tags);
    }

    public static void IncrementRotation(Guid userId, Guid oldRefreshId, Guid newRefreshId)
    {
        var tags = new System.Diagnostics.TagList
        {
            { "user_id", userId },
            { "old_refresh_id", oldRefreshId },
            { "new_refresh_id", newRefreshId }
        };
        RefreshRotations.Add(1, tags);
    }

    public static void IncrementReuseDenied(Guid? userId = null, Guid? refreshId = null)
    {
        var tags = new System.Diagnostics.TagList();
        if (userId.HasValue) tags.Add("user_id", userId.Value);
        if (refreshId.HasValue) tags.Add("refresh_id", refreshId.Value);
        RefreshReuseDenied.Add(1, tags);
    }

    public static void IncrementExpired(Guid? userId = null, Guid? refreshId = null)
    {
        var tags = new System.Diagnostics.TagList();
        if (userId.HasValue) tags.Add("user_id", userId.Value);
        if (refreshId.HasValue) tags.Add("refresh_id", refreshId.Value);
        RefreshExpired.Add(1, tags);
    }

    public static void IncrementPlaintextEmitted(Guid userId)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId } };
        RefreshPlaintextEmitted.Add(1, tags);
    }

    public static void IncrementPlaintextSuppressed(Guid userId)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId } };
        RefreshPlaintextSuppressed.Add(1, tags);
    }

    public static void IncrementLogoutSingle(Guid userId, bool tokenFound)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId }, { "token_found", tokenFound } };
        LogoutSingle.Add(1, tags);
    }

    public static void IncrementLogoutAll(Guid userId, int revokedCount)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId }, { "revoked_count", revokedCount } };
        LogoutAll.Add(1, tags);
    }

    // ----- Story 9 new increment helpers -----
    /// <summary>
    /// Record a successful login. membershipCount used for coarse-grained distribution (e.g., single vs multi-tenant user experience).
    /// </summary>
    public static void IncrementLoginSuccess(Guid userId, int membershipCount)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId }, { "memberships", membershipCount } };
        LoginSuccess.Add(1, tags);
    }

    /// <summary>
    /// Record a failed login attempt. Reason is a bounded set: missing_fields | unknown_user | invalid_credentials.
    /// userId optional if user resolved but password mismatch.
    /// </summary>
    public static void IncrementLoginFailure(string reason, Guid? userId = null)
    {
        var tags = new System.Diagnostics.TagList { { "reason", reason } };
        if (userId.HasValue) tags.Add("user_id", userId.Value);
        LoginFailure.Add(1, tags);
    }

    /// <summary>
    /// Record refresh success after rotation.
    /// </summary>
    public static void IncrementRefreshSuccess(Guid userId)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId } };
        RefreshSuccess.Add(1, tags);
    }

    /// <summary>
    /// Record a failed refresh attempt. Reason bounded: missing_refresh | refresh_body_disallowed | refresh_invalid | refresh_reuse | refresh_expired | refresh_forbidden_tenant | rate_limited.
    /// userId optional (may be unknown for invalid/missing cases).
    /// </summary>
    public static void IncrementRefreshFailure(string reason, Guid? userId = null)
    {
        var tags = new System.Diagnostics.TagList { { "reason", reason } };
        if (userId.HasValue) tags.Add("user_id", userId.Value);
        RefreshFailure.Add(1, tags);
    }

    /// <summary>
    /// Record a refresh request rejected due to rate limiting.
    /// </summary>
    public static void IncrementRefreshRateLimited()
    {
        var tags = new System.Diagnostics.TagList();
        RefreshRateLimited.Add(1, tags);
    }

    /// <summary>
    /// Observe login duration in milliseconds with success/failure outcome tag.
    /// </summary>
    public static void RecordLoginDuration(double ms, bool success)
    {
        var tags = new System.Diagnostics.TagList { { "outcome", success ? "success" : "failure" } };
        LoginDurationMs.Record(ms, tags);
    }

    /// <summary>
    /// Observe refresh duration in milliseconds with success/failure outcome tag.
    /// </summary>
    public static void RecordRefreshDuration(double ms, bool success)
    {
        var tags = new System.Diagnostics.TagList { { "outcome", success ? "success" : "failure" } };
        RefreshDurationMs.Record(ms, tags);
    }

}
