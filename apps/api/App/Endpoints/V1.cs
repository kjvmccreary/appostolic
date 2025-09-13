using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class V1
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var apiRoot = app.MapGroup("/api");
        // Anonymous auth endpoints
        apiRoot.MapPost("/auth/signup", async (AppDbContext db, Appostolic.Api.Application.Auth.IPasswordHasher hasher, SignupDto dto) =>
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
                return Results.BadRequest(new { error = "email and password are required" });

            var email = dto.Email.Trim();
            var existing = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
            if (existing is not null) return Results.Conflict(new { error = "email already exists" });

            var (hash, salt, iterations) = hasher.HashPassword(dto.Password);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordUpdatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // Handle invite path vs. self-serve personal tenant
            Guid tenantId;
            MembershipRole role;
            if (!string.IsNullOrWhiteSpace(dto.InviteToken))
            {
                var invite = await db.Invitations.AsNoTracking().FirstOrDefaultAsync(i => i.Token == dto.InviteToken);
                if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
                    return Results.BadRequest(new { error = "invalid or expired invite" });
                tenantId = invite.TenantId;
                role = invite.Role;
            }
            else
            {
                // Create personal tenant (slug: {localpart}-personal) if not exists
                var local = email.Split('@')[0].ToLowerInvariant();
                var slug = $"{local}-personal";
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Name == slug)
                             ?? new Tenant { Id = Guid.NewGuid(), Name = slug, CreatedAt = DateTime.UtcNow };
                if (await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant.Id) == false)
                {
                    db.Tenants.Add(tenant);
                    await db.SaveChangesAsync();
                }
                tenantId = tenant.Id;
                role = MembershipRole.Owner;
            }

            // Create membership under RLS by setting tenant context within a transaction
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                Role = role,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.tenant_id', {0}, true)", tenantId.ToString());
                db.Memberships.Add(membership);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            var tenantEntity = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
            return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Email, tenant = tenantEntity is null ? null : new { tenantEntity.Id, tenantEntity.Name } });
    }).AllowAnonymous();

        var api = apiRoot.RequireAuthorization();

        // GET /api/me
        api.MapGet("/me", (ClaimsPrincipal user) =>
        {
            var sub = user.FindFirstValue("sub") ?? string.Empty;
            var email = user.FindFirstValue("email") ?? string.Empty;
            var tenantId = user.FindFirstValue("tenant_id") ?? string.Empty;
            var tenantSlug = user.FindFirstValue("tenant_slug") ?? string.Empty;
            return Results.Ok(new { sub, email, tenant_id = tenantId, tenant_slug = tenantSlug });
        });

        // GET /api/tenants (tenant-scoped; returns current tenant)
        api.MapGet("/tenants", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdStr, out var tenantId)) return Results.BadRequest(new { error = "invalid tenant" });

            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            return Results.Ok(t is null ? Array.Empty<object>() : new[] { new { t.Id, t.Name } });
        });

        // GET /api/lessons (tenant-scoped)
        api.MapGet("/lessons", async (int? take, int? skip, AppDbContext db) =>
        {
            int takeVal = take.GetValueOrDefault(20);
            int skipVal = skip.GetValueOrDefault(0);
            var items = await db.Lessons.AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Skip(skipVal)
                .Take(takeVal)
                .ToListAsync();
            return Results.Ok(items);
        });

        // POST /api/lessons (tenant-scoped)
        api.MapPost("/lessons", async (ClaimsPrincipal user, AppDbContext db, NewLessonDto dto) =>
        {
            var tenantIdStr = user.FindFirstValue("tenant_id");
            var userIdStr = user.FindFirstValue("sub");
            if (!Guid.TryParse(tenantIdStr, out var tenantId) || !Guid.TryParse(userIdStr, out _))
                return Results.BadRequest(new { error = "invalid user/tenant" });

            var lesson = new Lesson
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Title = string.IsNullOrWhiteSpace(dto?.Title) ? "Untitled" : dto!.Title!,
                Status = LessonStatus.Draft,
                Audience = LessonAudience.All,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Lessons.Add(lesson);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lessons/{lesson.Id}", lesson);
        });

        return app;
    }

    public record NewLessonDto(string? Title);
    public record SignupDto(string Email, string Password, string? InviteToken = null);
}
