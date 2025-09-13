using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Infrastructure.Auth;

public class DevHeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public const string DevScheme = "Dev";

    public DevHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db,
        IConfiguration config)
        : base(options, logger, encoder)
    {
        _db = db;
        _config = config;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var path = Request.Path.ToString();
        var email = Request.Headers["x-dev-user"].FirstOrDefault();
        var slug = Request.Headers["x-tenant"].FirstOrDefault();

        var isInviteAccept = string.Equals(path, "/api/invites/accept", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(email) || (!isInviteAccept && string.IsNullOrWhiteSpace(slug)))
        {
            return AuthenticateResult.Fail("Missing x-dev-user or x-tenant headers");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return AuthenticateResult.Fail("User not found");
        }

        Guid? tenantId = null;
        string? tenantSlug = null;
        if (!isInviteAccept)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == slug);
            if (tenant is null)
                return AuthenticateResult.Fail("Tenant not found");

            var hasMembership = await _db.Memberships.AsNoTracking()
                .AnyAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id);
            if (!hasMembership)
                return AuthenticateResult.Fail("No membership for tenant");

            tenantId = tenant.Id;
            tenantSlug = tenant.Name;
        }

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email)
        };
        if (tenantId.HasValue && !string.IsNullOrEmpty(tenantSlug))
        {
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
            claims.Add(new Claim("tenant_slug", tenantSlug));
        }
        // Superadmin detection (Development convenience header + config allowlist)
        var superHeader = Request.Headers["x-superadmin"].FirstOrDefault();
        var isSuperHeader = string.Equals(superHeader, "true", StringComparison.OrdinalIgnoreCase);
        var allowlistRaw = _config["Auth:SuperAdminEmails"] ?? string.Empty; // comma/space separated
        var allow = allowlistRaw
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, user.Email, StringComparison.OrdinalIgnoreCase));
        if (isSuperHeader || allow)
        {
            claims.Add(new Claim("superadmin", "true"));
        }

        var identity = new ClaimsIdentity(claims, DevScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevScheme);

        return AuthenticateResult.Success(ticket);
    }
}
