using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Infrastructure.Auth.Jwt; // IJwtTokenService
using Appostolic.Api.Tests; // WebAppFactory
using Microsoft.Extensions.DependencyInjection; // CreateScope extension
using Microsoft.Extensions.Configuration; // IConfiguration for superadmin allowlist

namespace Appostolic.Api.AuthTests;

/// <summary>
/// Centralized test authentication seeding helper. Creates (or ensures) a user, optional tenant, and membership
/// then issues either a neutral or tenant-scoped JWT using the production <see cref="IJwtTokenService"/>.
/// This replaces scattered per-test seeding logic so tests converge on real auth flows while allowing
/// direct issuance where invoking /auth/login would add noise (e.g. for narrow unit-like integration tests).
/// Prefer calling the full HTTP flow via AuthTestClientFlow for end-to-end tests; use this only when
/// the test's focus is not the login/select pipeline itself.
/// </summary>
public static class TestAuthSeeder
{
    public const string DefaultPassword = "Password123!"; // keep consistent with AuthTestClientFlow

    /// <summary>
    /// Ensures a user row exists (optionally with password hash already seeded externally) and returns its id.
    /// Does not modify password fields (responsibility of dedicated password seeding helpers to mirror production hashing).
    /// </summary>
    private static async Task<(Guid userId, bool created)> EnsureUserAsync(AppDbContext db, string email)
    {
        var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        if (existing != null) return (existing.Id, false);
        var user = new User { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, true);
    }

    /// <summary>
    /// Ensures a tenant exists returning its id and name (slug == name in current model) without altering existing state.
    /// </summary>
    private static async Task<(Guid tenantId, string name, bool created)> EnsureTenantAsync(AppDbContext db, string tenantSlug)
    {
        var existing = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == tenantSlug);
        if (existing != null) return (existing.Id, existing.Name, false);
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = tenantSlug, CreatedAt = DateTime.UtcNow };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return (tenant.Id, tenant.Name, true);
    }

    /// <summary>
    /// Ensures a membership (user, tenant) exists with provided role flags. For simplicity we treat any admin-like
    /// flag combination as a single boolean set on Membership flags object (model specifics hidden behind dynamic/anon types here).
    /// </summary>
    private static async Task EnsureMembershipAsync(AppDbContext db, Guid userId, Guid tenantId, bool owner)
    {
        var existing = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId);
        if (existing != null) return;
        // Membership record shape (properties) derived from Program.cs model definitions. We reflect over the entity type to remain resilient
        // if the Roles property type changes (string vs flags enum). For now we attempt to assign common strings; if not present we skip.
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow
        };
        var rolesProp = membership.GetType().GetProperty("Roles");
        if (rolesProp != null && rolesProp.CanWrite)
        {
            try
            {
                if (rolesProp.PropertyType == typeof(string))
                {
                    rolesProp.SetValue(membership, owner ? "Owner" : "Learner");
                }
                else if (rolesProp.PropertyType.IsEnum && rolesProp.PropertyType.Name == nameof(Roles))
                {
                    // Assign full admin style flags for owner, else no flags (viewer) to mirror previous tests.
                    var value = owner ? (object)(Roles.TenantAdmin | Roles.Approver | Roles.Creator | Roles.Learner) : (object)Roles.None;
                    rolesProp.SetValue(membership, value);
                }
            }
            catch { /* swallow – helper best‑effort */ }
        }
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Issues a tenant-scoped access token creating user/tenant/membership as needed. Returns (token, userId, tenantId).
    /// </summary>
    public static async Task<(string token, Guid userId, Guid tenantId)> IssueTenantTokenAsync(WebAppFactory factory, string email, string tenantSlug, bool owner = true, IEnumerable<Claim>? extraClaims = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    (Guid userId, bool userCreated) = await EnsureUserAsync(db, email);
    (Guid tenantId, string _tenantName, bool tenantCreated) = await EnsureTenantAsync(db, tenantSlug);
        await EnsureMembershipAsync(db, userId, tenantId, owner);

        // Build claim list: include any caller-supplied claims, then inject superadmin if email in allowlist.
        var claimList = new List<Claim>();
        if (extraClaims != null) claimList.AddRange(extraClaims);

        // Mirror production allowlist logic so tests using direct issuance still model real auth behavior.
        // This fixes earlier gap where allowlisted user (kevin@example.com) did not receive the superadmin claim
        // when bypassing the /auth/login endpoint via TestAuthSeeder.
        var allowlistRaw = config["Auth:SuperAdminEmails"] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(allowlistRaw))
        {
            var parts = allowlistRaw
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (parts.Contains(email))
            {
                claimList.Add(new Claim("superadmin", "true"));
            }
        }

        var claims = claimList.ToArray();
        // token version currently always 0 in tests unless password rotation invalidates.
        var token = jwt.IssueTenantToken(userId.ToString(), tenantId, tenantSlug, 0, 0, email, claims);
        return (token, userId, tenantId);
    }

    /// <summary>
    /// Issues a neutral (no tenant) token ensuring user exists. Returns (token, userId).
    /// </summary>
    public static async Task<(string token, Guid userId)> IssueNeutralTokenAsync(WebAppFactory factory, string email, IEnumerable<Claim>? extraClaims = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        (Guid userId, bool userCreated) = await EnsureUserAsync(db, email);
        var claims = extraClaims ?? Array.Empty<Claim>();
        var token = jwt.IssueNeutralToken(userId.ToString(), 0, email, claims);
        return (token, userId);
    }
}
