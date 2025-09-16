using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.Application.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Appostolic.Api.Application.Privacy;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.App.Endpoints;

/// <summary>
/// Tenant-level settings management endpoints (branding, contact, etc.) plus logo upload/delete.
/// Routes infer the current tenant from the caller's tenant_id claim and require the TenantAdmin policy.
/// JSON merge semantics mirror user profile: objects are deep-merged, primitives/arrays replace, explicit nulls clear fields.
/// NOTE: We intentionally omit width/height metadata for logos for now; future image processing will populate those.
/// </summary>
public static class TenantSettingsEndpoints
{
    public static IEndpointRouteBuilder MapTenantSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tenants").RequireAuthorization().WithTags("Tenant");

        // GET /api/tenants/settings — fetch current tenant settings blob (or empty object)
        group.MapGet("settings", async (ClaimsPrincipal user, AppDbContext db, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
                return Results.BadRequest(new { error = "invalid tenant" });

            var tenant = await db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.Id, t.Name, t.Settings })
                .FirstOrDefaultAsync(ct);
            if (tenant is null) return Results.NotFound();
            // Return empty object when null for easier client consumption
            var settings = tenant.Settings is null ? new JsonObject() : JsonNode.Parse(tenant.Settings.RootElement.GetRawText());
            var logger = lf.CreateLogger("TenantSettings");
            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["tenant.id"] = tenant.Id,
                ["tenant.name"] = tenant.Name ?? string.Empty
            });
            logger.LogDebug("Fetched tenant settings");
            return Results.Ok(new { tenant.Id, tenant.Name, settings });
        }).RequireAuthorization("TenantAdmin").WithSummary("Get current tenant settings");

        // PUT /api/tenants/settings — merge patch into settings JSONB
        group.MapPut("settings", async (HttpRequest req, ClaimsPrincipal user, AppDbContext db, ILoggerFactory lf, IPIIHasher piiHasher, IOptions<PrivacyOptions> privacy, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
                return Results.BadRequest(new { error = "invalid tenant" });

            using var body = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object)
                return Results.BadRequest(new { error = "Expected JSON object" });

            var entity = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (entity is null) return Results.NotFound();

            var existingNode = entity.Settings is null
                ? new JsonObject()
                : JsonNode.Parse(entity.Settings.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            var patchNode = JsonNode.Parse(body.RootElement.GetRawText()) as JsonObject ?? new JsonObject();

            var merged = DeepMerge(existingNode, patchNode);

            using var ms = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(ms))
            {
                merged.WriteTo(writer);
            }
            ms.Position = 0;
            var mergedDoc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);

            var updated = entity with { Settings = mergedDoc };
            db.Tenants.Attach(updated);
            db.Entry(updated).Property(t => t.Settings).IsModified = true;
            await db.SaveChangesAsync(ct);

            var logger = lf.CreateLogger("TenantSettings");
            // If contact.email present, enrich scope with redacted+hashed form
            string? contactEmail = null;
            if (merged["contact"] is JsonObject cObj && cObj["email"] is JsonValue ev && ev.TryGetValue<string>(out var e)) contactEmail = e;
            using var scope = contactEmail is null
                ? logger.BeginScope(new Dictionary<string, object> { ["tenant.id"] = updated.Id })
                : LoggingPIIScope.BeginEmailScope(logger, contactEmail, piiHasher, privacy);
            logger.LogInformation("Updated tenant settings");
            return Results.Ok(new { updated.Id, settings = merged });
        }).RequireAuthorization("TenantAdmin").WithSummary("Update current tenant settings (merge)");

        // POST /api/tenants/logo — upload/replace tenant branding logo (PNG/JPEG/WEBP <=2MB)
        group.MapPost("logo", async (HttpRequest req, ClaimsPrincipal user, AppDbContext db, IObjectStorageService storage, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
                return Results.BadRequest(new { error = "invalid tenant" });

            if (!req.HasFormContentType)
                return Results.BadRequest(new { error = "Expected multipart/form-data" });

            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "File is required" });

            var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
            var allowed = new[] { "image/png", "image/jpeg", "image/webp" };
            if (!allowed.Contains(contentType))
                return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

            const long maxSize = 2 * 1024 * 1024; // 2MB
            if (file.Length > maxSize)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var ext = contentType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => string.Empty
            };

            var entity = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (entity is null) return Results.NotFound();

            // Parse existing settings and capture old logo key for cleanup
            var settingsNode = entity.Settings is null
                ? new JsonObject()
                : JsonNode.Parse(entity.Settings.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            string? oldKey = settingsNode?["branding"] is JsonObject brandingObj && brandingObj["logo"] is JsonObject logoObj && logoObj["key"] is JsonValue keyVal && keyVal.TryGetValue<string>(out var k) ? k : null;

            var key = $"tenants/{tenantId}/logo{ext}";
            await using var stream = file.OpenReadStream();
            var (url, storedKey) = await storage.UploadAsync(key, contentType, stream, ct);

            // Ensure settingsNode is non-null (it is always a JsonObject by construction above)
            var branding = settingsNode is not null && settingsNode["branding"] is JsonObject bObj ? bObj : new JsonObject();
            branding["logo"] = new JsonObject
            {
                ["url"] = url,
                ["key"] = storedKey,
                ["mime"] = contentType
                // width / height placeholders intentionally omitted until image processing story
            };
            // settingsNode is guaranteed non-null (created as new JsonObject when null earlier)
            settingsNode!["branding"] = branding;

            using var ms = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(ms))
            {
                settingsNode.WriteTo(writer);
            }
            ms.Position = 0;
            var updatedDoc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
            var updated = entity with { Settings = updatedDoc };
            db.Tenants.Attach(updated);
            db.Entry(updated).Property(t => t.Settings).IsModified = true;
            await db.SaveChangesAsync(ct);

            // Best-effort cleanup of old logo asset if key differs
            if (!string.IsNullOrWhiteSpace(oldKey) && oldKey != storedKey)
            {
                try { await storage.DeleteAsync(oldKey, ct); } catch { /* swallow */ }
            }

            return Results.Ok(new { logo = new { url, key = storedKey, mime = contentType } });
        }).RequireAuthorization("TenantAdmin").WithSummary("Upload tenant branding logo");

        // DELETE /api/tenants/logo — remove tenant logo and delete underlying object
        group.MapDelete("logo", async (ClaimsPrincipal user, AppDbContext db, IObjectStorageService storage, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId))
                return Results.BadRequest(new { error = "invalid tenant" });

            var entity = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (entity is null) return Results.NotFound();

            if (entity.Settings is null)
                return Results.NoContent(); // nothing to delete

            var settingsNode = JsonNode.Parse(entity.Settings.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            string? keyToDelete = null;
            if (settingsNode["branding"] is JsonObject branding && branding["logo"] is JsonObject logo)
            {
                if (logo["key"] is JsonValue kv && kv.TryGetValue<string>(out var k)) keyToDelete = k;
                branding.Remove("logo");
                if (!branding.Any())
                    settingsNode.Remove("branding");
            }
            else
            {
                return Results.NoContent();
            }

            using var ms = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(ms))
            {
                settingsNode.WriteTo(writer);
            }
            ms.Position = 0;
            var updatedDoc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
            var updated = entity with { Settings = updatedDoc };
            db.Tenants.Attach(updated);
            db.Entry(updated).Property(t => t.Settings).IsModified = true;
            await db.SaveChangesAsync(ct);

            if (!string.IsNullOrWhiteSpace(keyToDelete))
            {
                try { await storage.DeleteAsync(keyToDelete, ct); } catch { /* ignore */ }
            }

            return Results.NoContent();
        }).RequireAuthorization("TenantAdmin").WithSummary("Delete tenant branding logo");

        return app;
    }

    /// <summary>
    /// Deep merges patch into target:
    /// - JsonObjects are merged recursively
    /// - Arrays and primitive values from the patch replace existing values
    /// - Explicit nulls in the patch clear fields in the target
    /// Clones assigned nodes to avoid "node already has a parent" exceptions.
    /// NOTE: Duplicated from UserProfileEndpoints; consider refactor to shared helper (tracked in TODO).
    /// </summary>
    // TODO (post-MVP): Extract this duplicated DeepMerge (and the one in UserProfileEndpoints)
    // into a single shared helper (e.g., JsonMerge.DeepMerge) with unit tests. Keep semantics:
    // objects merge, arrays/primitives replace, explicit null clears. Add depth limit guard if
    // payload nesting grows in future stories.
    private static JsonObject DeepMerge(JsonObject target, JsonObject patch)
    {
        foreach (var kv in patch)
        {
            var key = kv.Key;
            var pVal = kv.Value;
            if (pVal is null)
            {
                target[key] = null; // allow explicit nulling of fields
                continue;
            }

            if (pVal is JsonObject pObj)
            {
                var tObj = target[key] as JsonObject ?? new JsonObject();
                target[key] = DeepMerge(tObj, pObj);
            }
            else
            {
                target[key] = pVal!.DeepClone();
            }
        }
        return target;
    }
}
