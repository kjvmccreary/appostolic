namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Story 12: Configurable CSRF protection for refresh/login flows when operating with cross-site embedding (SameSite=None) or multi-tab security.
/// When Enabled, server expects an anti-CSRF token (double-submit cookie technique) for state-changing auth endpoints (login, refresh, select-tenant, logout).
/// </summary>
public class CsrfOptions
{
    /// <summary>
    /// Master enable flag. If false, no CSRF validation performed.
    /// </summary>
    public bool Enabled { get; set; } = false; // AUTH__CSRF__ENABLED

    /// <summary>
    /// Name of the cookie carrying the anti-CSRF token (unprotected/random). Default: "csrf".
    /// </summary>
    public string CookieName { get; set; } = "csrf"; // AUTH__CSRF__COOKIE_NAME

    /// <summary>
    /// Name of the header expected to mirror the cookie value. Default: "X-CSRF".
    /// </summary>
    public string HeaderName { get; set; } = "X-CSRF"; // AUTH__CSRF__HEADER_NAME

    /// <summary>
    /// If true, automatically issue a CSRF cookie (if missing) on GET /api/auth/csrf and on login response for convenience.
    /// </summary>
    public bool AutoIssue { get; set; } = true; // AUTH__CSRF__AUTO_ISSUE

    /// <summary>
    /// TTL (minutes) for the CSRF cookie (sliding) when auto-issuing. 0 or negative => session cookie.
    /// </summary>
    public int CookieTtlMinutes { get; set; } = 120; // AUTH__CSRF__COOKIE_TTL_MINUTES
}
