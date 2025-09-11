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
        var api = app.MapGroup("/api").RequireAuthorization();

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
}
