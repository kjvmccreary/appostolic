using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;

namespace Appostolic.Api.App.Endpoints;

/// <summary>
/// Development-only authentication helper endpoints to streamline Swagger usage.
/// Provides: POST /dev/auth/login (email, password|magic token) -> dev token, and
/// POST /dev/auth/select-tenant (dev token, tenantSlug) -> tenant-scoped dev token.
/// These tokens are opaque and only valid in Development. They simulate a bearer workflow
/// over the existing Dev header authentication scheme without introducing full JWT issuance.
/// </summary>
public static class DevAuthEndpoints
{
    private record LoginRequest(string Email, string? Password, string? MagicToken);
    private record LoginResponse(string DevToken, string Email, IEnumerable<object> Memberships);
    private record TenantSelectRequest(string DevToken, string TenantSlug);
    private record TenantSelectResponse(string DevToken, string Email, Guid TenantId, string TenantSlug, int Roles);

    // In-memory ephemeral token store (singleton for app lifetime). For multi-instance scaling this would not work,
    // but it's strictly Development only.
    private class DevTokenStore
    {
        private readonly Dictionary<string, (Guid UserId, string Email, Guid? TenantId, string? TenantSlug)> _tokens = new();
        public string Issue(Guid userId, string email, Guid? tenantId, string? tenantSlug)
        {
            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())[0..22];
            _tokens[token] = (userId, email, tenantId, tenantSlug);
            return token;
        }
        public (Guid UserId, string Email, Guid? TenantId, string? TenantSlug)? Get(string token)
            => _tokens.TryGetValue(token, out var v) ? v : null;
    }

    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var env = app.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsDevelopment()) return app; // Only map in Development

        var group = app.MapGroup("/dev/auth");
        group.WithTags("DevAuth");

        // Register singleton store lazily
        var store = app.ServiceProvider.GetService<DevTokenStore>() ??
                    ActivatorUtilities.CreateInstance<DevTokenStore>(app.ServiceProvider);
        // ensure added to DI root
        if (app.ServiceProvider.GetService<DevTokenStore>() is null)
        {
            // can't modify services post-build normally; store as static for simplicity
            GlobalStore.Instance = store;
        }

        group.MapPost("/login", async (LoginRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email)) return Results.BadRequest(new { error = "email required" });
            var email = req.Email.Trim();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null) return Results.BadRequest(new { error = "invalid credentials" });
            // For now we ignore password validation (dev convenience) - could be extended.
            var memberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == user.Id)
                .Join(db.Tenants.AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => new { tenantId = t.Id, tenantSlug = t.Name, roles = (int)m.Roles })
                .ToListAsync();
            var token = GlobalStore.Instance!.Issue(user.Id, user.Email, null, null);
            return Results.Ok(new LoginResponse(token, user.Email, memberships));
        }).WithSummary("Dev Login (email -> dev token)").WithDescription("Obtain a temporary development token for Swagger. Use /dev/auth/select-tenant next.").AllowAnonymous();

        group.MapPost("/select-tenant", async (TenantSelectRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.DevToken)) return Results.BadRequest(new { error = "devToken required" });
            if (string.IsNullOrWhiteSpace(req.TenantSlug)) return Results.BadRequest(new { error = "tenantSlug required" });
            var record = GlobalStore.Instance!.Get(req.DevToken);
            if (record is null) return Results.BadRequest(new { error = "invalid devToken" });
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == req.TenantSlug);
            if (tenant is null) return Results.BadRequest(new { error = "tenant not found" });
            var hasMembership = await db.Memberships.AsNoTracking().AnyAsync(m => m.UserId == record.Value.UserId && m.TenantId == tenant.Id);
            if (!hasMembership) return Results.Forbid();
            var membership = await db.Memberships.AsNoTracking().FirstAsync(m => m.UserId == record.Value.UserId && m.TenantId == tenant.Id);
            var newToken = GlobalStore.Instance!.Issue(record.Value.UserId, record.Value.Email, tenant.Id, tenant.Name);
            return Results.Ok(new TenantSelectResponse(newToken, record.Value.Email, tenant.Id, tenant.Name, (int)membership.Roles));
        }).WithSummary("Dev Tenant Select (token + tenant)").WithDescription("Exchange a dev token plus tenantSlug for a tenant-scoped dev token carrying implied membership.").AllowAnonymous();

        return app;
    }

    private static class GlobalStore
    {
        public static DevTokenStore? Instance { get; set; } = new DevTokenStore();
    }
}
