using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.Infrastructure.Auth;

public sealed class RoleRequirement(Roles required) : IAuthorizationRequirement
{
    public Roles Required { get; } = required;
}

public sealed class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public RoleAuthorizationHandler(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
    {
    // Authorization evaluation for role requirement (debug logging removed post-migration)
        var http = _http.HttpContext;
        if (http is null)
        {
            Console.WriteLine("[api][roles][early-exit] httpContext=null");
            return;
        }
        var traceEnabled = Environment.GetEnvironmentVariable("ROLE_TRACE")?.ToLower() == "true";

        // Some environments map 'sub' => ClaimTypes.NameIdentifier; support both.
        var userIdStr = context.User.FindFirstValue("sub")
                        ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            Console.WriteLine($"[api][roles][early-exit] invalid-user-id sub|nameidentifier={userIdStr}");
            return;
        }

        // Resolve tenantId from Items, then claim, then header fallback
        Guid tenantId;
        if (http.Items.TryGetValue("TenantId", out var tidVal) && tidVal is Guid tid)
        {
            tenantId = tid;
        }
        else if (Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var claimTid))
        {
            tenantId = claimTid;
        }
        else if (Guid.TryParse(http.Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var headerTid))
        {
            tenantId = headerTid;
        }
        else
        {
            Console.WriteLine("[api][roles][early-exit] tenantId-unresolved");
            return;
        }

        var membership = await _db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId && m.TenantId == tenantId)
            .Select(m => new { m.Roles })
            .SingleOrDefaultAsync(http.RequestAborted);

        // Flags-only authorization (Story 4 Phase 2): no legacy Role fallback.
        var have = membership?.Roles ?? Roles.None;

        if (traceEnabled)
        {
            // Optional trace hook: could add structured logging when ROLE_TRACE=true
        }

        // Record the required roles for downstream error formatting (status code pages)
        http.Items["AuthRequiredRoles"] = requirement.Required.ToString();
        if ((have & requirement.Required) == requirement.Required)
        {
            context.Succeed(requirement);
        }
    }
}
