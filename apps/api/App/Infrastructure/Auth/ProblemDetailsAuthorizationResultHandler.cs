using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Appostolic.Api.Infrastructure.Auth;

/// Produces a consistent ProblemDetails JSON body for 403 (Forbid) results from policy authorization.
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        // If authorization succeeded or produced a challenge, use the default behavior
        if (!authorizeResult.Forbidden)
        {
            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        // Build a ProblemDetails-style payload for 403
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        // Try to extract tenant id from Items, claim, or header for observability
        string? tenantIdStr = null;
        if (context.Items.TryGetValue("TenantId", out var tid) && tid is Guid gid)
        {
            tenantIdStr = gid.ToString();
        }
        else if (Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var claimTid))
        {
            tenantIdStr = claimTid.ToString();
        }
        else if (Guid.TryParse(context.Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var headerTid))
        {
            tenantIdStr = headerTid.ToString();
        }

        // Extract required role names from RoleRequirement requirements in the policy (if any)
        var requiredRoles = policy.Requirements
            .OfType<RoleRequirement>()
            .Select(r => r.Required.ToString())
            .Distinct()
            .ToArray();

        var problem = new
        {
            type = "https://httpstatuses.com/403",
            title = "Forbidden",
            status = 403,
            detail = requiredRoles.Length > 0
                ? ($"Missing required role: {string.Join(", ", requiredRoles)}")
                : "You do not have permission to perform this action.",
            extensions = new
            {
                tenantId = tenantIdStr,
                requiredRoles
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
