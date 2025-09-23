using System.Text.Json;
using Appostolic.Api.Application.Auth;

namespace Appostolic.Api.App.Middleware;

/// <summary>
/// Rejects requests that include deprecated development auth headers when dev headers are disabled.
/// </summary>
/// <remarks>
/// RDH Story 3 (Deprecation Mode).
/// If <c>AUTH__ALLOW_DEV_HEADERS</c> is set to false (the default) and either <c>x-dev-user</c> or <c>x-tenant</c>
/// header is present, the middleware short-circuits the pipeline with HTTP 401 and JSON body
/// <c>{ "code": "dev_headers_deprecated" }</c>. A metric counter is incremented for observability.
/// Positioned before authentication so no downstream auth handlers execute. In Story 4 this middleware
/// and remaining legacy handler logic will be removed and tests will then assert <c>dev_headers_removed</c>.
/// </remarks>
public class DevHeadersDeprecationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _allowDevHeaders;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public DevHeadersDeprecationMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _allowDevHeaders = (config["AUTH__ALLOW_DEV_HEADERS"] ?? Environment.GetEnvironmentVariable("AUTH__ALLOW_DEV_HEADERS") ?? "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_allowDevHeaders)
        {
            // If either legacy header present, block early.
            if (context.Request.Headers.ContainsKey("x-dev-user") || context.Request.Headers.ContainsKey("x-tenant"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var payload = new { code = "dev_headers_deprecated" };
                AuthMetrics.IncrementDevHeadersDeprecated(context.Request.Path.Value);
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
                return;
            }
        }

        await _next(context);
    }
}