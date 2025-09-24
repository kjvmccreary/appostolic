using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace Appostolic.Api.Application.Auth;

/// <summary>
/// Story 12: Simple double-submit cookie CSRF service.
/// Generates a random token, sets cookie, and validates that header matches cookie on protected endpoints.
/// NOTE: Not cryptographically bound to session intentionally (stateless). Hardening (token binding) can be added later if required.
/// </summary>
public interface ICsrfService
{
    string EnsureToken(HttpContext http);
    bool Validate(HttpContext http, out string? errorCode);
}

public class CsrfService : ICsrfService
{
    private readonly IOptions<CsrfOptions> _opts;
    private readonly ILogger<CsrfService> _logger;
    public CsrfService(IOptions<CsrfOptions> opts, ILogger<CsrfService> logger)
    {
        _opts = opts;
        _logger = logger;
    }

    public string EnsureToken(HttpContext http)
    {
        var o = _opts.Value;
        if (http.Request.Cookies.TryGetValue(o.CookieName, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;
        var token = Generate();
        var opts = new CookieOptions
        {
            HttpOnly = false, // must be readable by JS for header mirroring (double-submit pattern)
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.None, // explicit none; outer ingress may adjust if not cross-site
            Path = "/",
        };
        if (_opts.Value.CookieTtlMinutes > 0)
        {
            opts.Expires = DateTimeOffset.UtcNow.AddMinutes(_opts.Value.CookieTtlMinutes);
        }
        http.Response.Cookies.Append(o.CookieName, token, opts);
        return token;
    }

    public bool Validate(HttpContext http, out string? errorCode)
    {
        errorCode = null;
        var o = _opts.Value;
        if (!o.Enabled) return true;
        if (!http.Request.Cookies.TryGetValue(o.CookieName, out var cookie) || string.IsNullOrWhiteSpace(cookie))
        {
            errorCode = "csrf_missing_cookie";
            AuthMetrics.IncrementCsrfFailure("missing_cookie");
            return false;
        }
        if (!http.Request.Headers.TryGetValue(o.HeaderName, out var headerVals))
        {
            errorCode = "csrf_missing_header";
            AuthMetrics.IncrementCsrfFailure("missing_header");
            return false;
        }
        var header = headerVals.ToString();
        if (!TimingSafeEquals(cookie, header))
        {
            errorCode = "csrf_mismatch";
            AuthMetrics.IncrementCsrfFailure("mismatch");
            return false;
        }
        AuthMetrics.IncrementCsrfValidationSuccess();
        return true;
    }

    private static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TimingSafeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
