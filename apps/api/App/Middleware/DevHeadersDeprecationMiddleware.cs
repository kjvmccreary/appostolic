using System.Text.Json;
using Appostolic.Api.Application.Auth;

namespace Appostolic.Api.App.Middleware;

/// <summary>
/// Permanently rejects any request that still sends legacy development auth headers (Story 4 removal).
/// Always short-circuits with HTTP 401 and JSON body { "code": "dev_headers_removed" }.
/// This enforces a single authentication path (JWT) and provides a deterministic error
/// code for any stale scripts/tools still sending x-dev-user/x-tenant headers.
/// NOTE: Middleware retained temporarily (instead of full deletion) to emit the stable error code
/// while client tooling is updated. It can be fully removed in a later cleanup story.
/// </summary>
public class DevHeadersDeprecationMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public DevHeadersDeprecationMiddleware(RequestDelegate next, IConfiguration _)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // If either legacy header present, block early with permanent removal code.
        if (context.Request.Headers.ContainsKey("x-dev-user") || context.Request.Headers.ContainsKey("x-tenant"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var payload = new { code = "dev_headers_removed" };
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
            return;
        }

        await _next(context);
    }
}