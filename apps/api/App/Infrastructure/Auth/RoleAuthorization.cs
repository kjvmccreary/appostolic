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
            .Select(m => new { m.Roles, m.Role })
            .SingleOrDefaultAsync(http.RequestAborted);

        // Prefer Roles flags as the source of truth. Fall back to legacy Role only when Roles == None.
        // This ensures demotions performed via roles flags take effect even if the legacy Role was not updated.
        var have = Roles.None;
        if (membership is null)
        {
            have = Roles.None;
        }
        else if (membership.Roles != Roles.None)
        {
            have = membership.Roles;
        }
        else
        {
            switch (membership.Role)
            {
                case MembershipRole.Owner:
                case MembershipRole.Admin:
                    have = Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner;
                    break;
                case MembershipRole.Editor:
                    have = Roles.Creator | Roles.Learner;
                    break;
                case MembershipRole.Viewer:
                default:
                    have = Roles.Learner;
                    break;
            }
        }

        if (traceEnabled)
        {
            Console.WriteLine($"[api][roles][trace] user={userId} tenant={tenantId} required={requirement.Required} have={have} rawRoles={(membership?.Roles).GetValueOrDefault()} legacyRole={(membership?.Role).GetValueOrDefault()}" );
        }

        // Record the required roles for downstream error formatting (status code pages)
        http.Items["AuthRequiredRoles"] = requirement.Required.ToString();
        if ((have & requirement.Required) == requirement.Required)
        {
            context.Succeed(requirement);
        }
    }
}
