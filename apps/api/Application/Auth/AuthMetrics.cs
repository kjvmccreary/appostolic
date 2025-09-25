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

    // Story 3: Limiter evaluation latency (single evaluation per request after refactor). Outcome tag: hit (under), block, dryrun_block (would block but dry-run).
    public static readonly Histogram<double> RefreshLimiterEvaluationMs = Meter.CreateHistogram<double>(
        name: "auth.refresh.limiter.evaluation_ms",
        unit: "ms",
        description: "Latency of refresh rate limiter evaluation (single evaluation). outcome=hit|block|dryrun_block" 
    );

    // Story 4 (Key Rotation): Per-key signing visibility & validation probe failures.
    public static readonly Counter<long> KeyRotationTokensSigned = Meter.CreateCounter<long>(
        name: "auth.jwt.key_rotation.tokens_signed",
        unit: "{token}",
        description: "Count of access tokens signed by a given active signing key. Tag: kid=<key id>"
    );

    public static readonly Counter<long> KeyRotationValidationFailure = Meter.CreateCounter<long>(
        name: "auth.jwt.key_rotation.validation_failure",
        unit: "{event}",
        description: "Count of failures during multi-key verification health probe. Tag: phase=issue|validate" 
    );

    // Story 5: Tracing span enrichment counter (counts number of auth-related spans we enriched with attributes)
    public static readonly Counter<long> TraceEnrichedSpans = Meter.CreateCounter<long>(
        name: "auth.trace.enriched_spans",
        unit: "{span}",
        description: "Count of auth spans enriched with standardized attributes (auth.user_id, auth.outcome, etc). Tag: span_kind=server|internal" 
    );

    // Story 6: Security events emitted counter
    public static readonly Counter<long> SecurityEventsEmitted = Meter.CreateCounter<long>(
        name: "auth.security.events_emitted",
        unit: "{event}",
        description: "Count of structured auth security events emitted. Tag: type=<event_type>" 
    );

    // Story 8: Session enumeration & selective revoke
    public static readonly Counter<long> SessionEnumerationRequests = Meter.CreateCounter<long>(
        name: "auth.session.enumeration.requests",
        unit: "{request}",
        description: "Count of session enumeration (list) requests. outcome=success|disabled" 
    );
    public static readonly Counter<long> SessionRevokeRequests = Meter.CreateCounter<long>(
        name: "auth.session.revoke.requests",
        unit: "{request}",
        description: "Count of per-session revoke requests. outcome=success|not_found|forbidden" 
    );

    // Story 17/18: Device naming adoption visibility (increments first time a non-null device name stored for a session)
    public static readonly Counter<long> SessionDeviceNamed = Meter.CreateCounter<long>(
        name: "auth.session.device_named",
        unit: "{session}",
        description: "Count of sessions assigned a human-readable device name (first assignment only)."
    );

    // Story 9: Admin / Tenant forced logout
    public static readonly Counter<long> AdminForcedLogoutRequests = Meter.CreateCounter<long>(
        name: "auth.admin.forced_logout.requests",
        unit: "{request}",
        description: "Count of forced logout requests. scope=user|tenant; outcome=success|not_found|forbidden|disabled" 
    );
    public static readonly Counter<long> AdminForcedLogoutSessionsRevoked = Meter.CreateCounter<long>(
        name: "auth.admin.forced_logout.sessions_revoked",
        unit: "{session}",
        description: "Count of sessions (refresh tokens) revoked via forced logout. scope=user|tenant" 
    );

    // Story 10: TokenVersion cache + validation latency
    public static readonly Counter<long> TokenVersionCacheHit = Meter.CreateCounter<long>(
        name: "auth.token_version.cache_hit",
        unit: "{event}",
        description: "Count of TokenVersion validation operations served from in-memory cache."
    );
    public static readonly Counter<long> TokenVersionCacheMiss = Meter.CreateCounter<long>(
        name: "auth.token_version.cache_miss",
        unit: "{event}",
        description: "Count of TokenVersion validation operations that required a DB lookup (cold, expired, or disabled)."
    );
    public static readonly Histogram<double> TokenValidationLatencyMs = Meter.CreateHistogram<double>(
        name: "auth.token_validation.latency_ms",
        unit: "ms",
        description: "Latency (ms) of TokenVersion validation (includes cache access + optional DB lookup)."
    );

    // Story 11: Sliding refresh max lifetime enforcement
    public static readonly Counter<long> RefreshMaxLifetimeExceeded = Meter.CreateCounter<long>(
        name: "auth.refresh.max_lifetime_exceeded",
        unit: "{event}",
        description: "Count of refresh attempts denied because the session exceeded the configured absolute max lifetime (sliding window cap)."
    );

    // Story 12: CSRF validation visibility
    public static readonly Counter<long> CsrfFailures = Meter.CreateCounter<long>(
        name: "auth.csrf.failures",
        unit: "{event}",
        description: "Count of CSRF validation failures. reason=missing_cookie|missing_header|mismatch" 
    );
    public static readonly Counter<long> CsrfValidations = Meter.CreateCounter<long>(
        name: "auth.csrf.validations",
        unit: "{event}",
        description: "Count of CSRF validations performed (successful)." 
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

    /// <summary>
    /// Increment when a refresh attempt exceeds absolute max lifetime window.
    /// </summary>
    public static void IncrementRefreshMaxLifetimeExceeded(Guid? userId = null, Guid? refreshId = null)
    {
        var tags = new System.Diagnostics.TagList();
        if (userId.HasValue) tags.Add("user_id", userId.Value);
        if (refreshId.HasValue) tags.Add("refresh_id", refreshId.Value);
        RefreshMaxLifetimeExceeded.Add(1, tags);
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
    /// Record a failed refresh attempt. Reason bounded: missing_refresh | refresh_invalid | refresh_reuse | refresh_expired | refresh_forbidden_tenant | refresh_rate_limited | refresh_max_lifetime_exceeded.
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

    /// <summary>
    /// Record the latency of a refresh limiter evaluation with outcome tag (hit|block|dryrun_block).
    /// </summary>
    public static void RecordRefreshLimiterEvaluation(double ms, string outcome)
    {
        var tags = new System.Diagnostics.TagList { { "outcome", outcome } };
        RefreshLimiterEvaluationMs.Record(ms, tags);
    }

    /// <summary>
    /// Record that a token was signed by the current active key (kid).
    /// </summary>
    public static void IncrementKeyRotationTokenSigned(string keyId)
    {
        var tags = new System.Diagnostics.TagList { { "kid", keyId } };
        KeyRotationTokensSigned.Add(1, tags);
    }

    /// <summary>
    /// Record a failure in the VerifyAllSigningKeys probe. phase: issue | validate.
    /// </summary>
    public static void IncrementKeyRotationValidationFailure(string phase)
    {
        var tags = new System.Diagnostics.TagList { { "phase", phase } };
        KeyRotationValidationFailure.Add(1, tags);
    }

    /// <summary>
    /// Record a CSRF validation failure with bounded reason.
    /// </summary>
    public static void IncrementCsrfFailure(string reason)
    {
        var tags = new System.Diagnostics.TagList { { "reason", reason } };
        CsrfFailures.Add(1, tags);
    }

    /// <summary>
    /// Record a successful CSRF validation.
    /// </summary>
    public static void IncrementCsrfValidationSuccess()
    {
        var tags = new System.Diagnostics.TagList();
        CsrfValidations.Add(1, tags);
    }

    /// <summary>
    /// Record that an auth span was enriched. spanKind: server|internal. outcome optional (success|failure) for server spans.
    /// </summary>
    public static void IncrementTraceEnriched(string spanKind, string? outcome = null)
    {
        var tags = new System.Diagnostics.TagList { { "span_kind", spanKind } };
        if (!string.IsNullOrWhiteSpace(outcome)) tags.Add("outcome", outcome);
        TraceEnrichedSpans.Add(1, tags);
    }

    /// <summary>
    /// Record that a structured security event was emitted.
    /// </summary>
    public static void IncrementSecurityEvent(string type)
    {
        var tags = new System.Diagnostics.TagList { { "type", type } };
        SecurityEventsEmitted.Add(1, tags);
    }

    /// <summary>
    /// Record a forced logout request attempt.
    /// </summary>
    public static void IncrementAdminForcedLogoutRequest(string scope, string outcome)
    {
        var tags = new System.Diagnostics.TagList { { "scope", scope }, { "outcome", outcome } };
        AdminForcedLogoutRequests.Add(1, tags);
    }

    /// <summary>
    /// Record the number of sessions revoked by a forced logout.
    /// </summary>
    public static void AddAdminForcedLogoutSessionsRevoked(string scope, int count)
    {
        if (count <= 0) return;
        var tags = new System.Diagnostics.TagList { { "scope", scope } };
        AdminForcedLogoutSessionsRevoked.Add(count, tags);
    }

    /// <summary>
    /// Record that a device name was set for a session for the first time.
    /// </summary>
    public static void IncrementSessionDeviceNamed(Guid userId, Guid refreshId)
    {
        var tags = new System.Diagnostics.TagList { { "user_id", userId }, { "refresh_id", refreshId } };
        SessionDeviceNamed.Add(1, tags);
    }

}
