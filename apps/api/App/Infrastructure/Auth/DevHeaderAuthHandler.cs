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

    public const string DevScheme = "Dev";

    public DevHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers["x-dev-user"].FirstOrDefault();
        var slug = Request.Headers["x-tenant"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(slug))
        {
            return AuthenticateResult.Fail("Missing x-dev-user or x-tenant headers");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            return AuthenticateResult.Fail("User not found");
        }

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == slug);
        if (tenant is null)
        {
            return AuthenticateResult.Fail("Tenant not found");
        }

        var hasMembership = await _db.Memberships.AsNoTracking()
            .AnyAsync(m => m.UserId == user.Id && m.TenantId == tenant.Id);
        if (!hasMembership)
        {
            return AuthenticateResult.Fail("No membership for tenant");
        }

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email),
            new("tenant_id", tenant.Id.ToString()),
            new("tenant_slug", tenant.Name)
        };
        var identity = new ClaimsIdentity(claims, DevScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DevScheme);

        return AuthenticateResult.Success(ticket);
    }
}
