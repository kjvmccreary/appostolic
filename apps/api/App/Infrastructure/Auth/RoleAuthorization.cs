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
        var http = _http.HttpContext;
        if (http is null)
            return;
        var traceEnabled = Environment.GetEnvironmentVariable("ROLE_TRACE")?.ToLower() == "true";

        var userIdStr = context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
            return;

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
            Console.WriteLine($"[api][roles][trace] user={userId} tenant={tenantId} required={requirement.Required} have={have} rawRoles={(membership?.Roles).GetValueOrDefault()}" );
        }

        // Record the required roles for downstream error formatting (status code pages)
        http.Items["AuthRequiredRoles"] = requirement.Required.ToString();
        if ((have & requirement.Required) == requirement.Required)
        {
            context.Succeed(requirement);
        }
    }
}
