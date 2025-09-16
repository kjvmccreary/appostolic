using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Application.Auth;
using Appostolic.Api.Application.Storage;

namespace Appostolic.Api.App.Endpoints;

public static class UserProfileEndpoints
{
    // Maps user profile endpoints under /api/users. Requires authorization.
    public static IEndpointRouteBuilder MapUserProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization().WithTags("User");

        // GET /api/users/me
        group.MapGet("me", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
                return Results.Unauthorized();

            var me = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.Email, u.Profile })
                .FirstOrDefaultAsync(ct);
            if (me is null) return Results.NotFound();
            return Results.Ok(me);
        })
        .WithSummary("Get current user profile");

        // PUT /api/users/me — merge patch into profile
        group.MapPut("me", async (HttpRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
                return Results.Unauthorized();

            // Read body as JSON
            using var body = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object)
                return Results.BadRequest(new { error = "Expected JSON object" });

            // Load without tracking to safely create an immutable copy for update
            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (entity is null) return Results.NotFound();

            // Merge existing profile (object or empty) with patch
            var existingNode = entity.Profile is null
                ? new JsonObject()
                : JsonNode.Parse(entity.Profile.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            var patchNode = JsonNode.Parse(body.RootElement.GetRawText()) as JsonObject ?? new JsonObject();

            // Optional normalization/validation: trim strings and validate URLs in social fields
            NormalizeProfilePatch(patchNode);

            var merged = DeepMerge(existingNode, patchNode);

            // Convert back to JsonDocument for storage
            using var ms = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(ms))
            {
                merged.WriteTo(writer);
            }
            ms.Position = 0;
            var mergedDoc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);

            var updated = entity with { Profile = mergedDoc };
            db.Users.Attach(updated);
            db.Entry(updated).Property(u => u.Profile).IsModified = true;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { updated.Id, updated.Email, updated.Profile });
        })
        .WithSummary("Update current user profile (merge)");

        // POST /api/users/me/password — change password
        group.MapPost("me/password", async (ClaimsPrincipal user, AppDbContext db, IPasswordHasher hasher, HttpRequest req, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
                return Results.Unauthorized();

            var dto = await req.ReadFromJsonAsync<PasswordChangeDto>(cancellationToken: ct);
            if (dto is null || string.IsNullOrWhiteSpace(dto.CurrentPassword) || string.IsNullOrWhiteSpace(dto.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required" });

            // Basic strength policy: length >= 8, contains letter and digit
            if (!IsStrongPassword(dto.NewPassword))
                return Results.UnprocessableEntity(new { error = "Weak password: min 8 chars with letters and digits required" });

            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (entity is null) return Results.NotFound();

            var hasExisting = entity.PasswordHash is not null && entity.PasswordSalt is not null;
            var valid = hasExisting && hasher.Verify(dto.CurrentPassword, entity.PasswordHash!, entity.PasswordSalt!, iterations: 0);
            if (!valid)
                return Results.BadRequest(new { error = "Invalid current password" });

            var (hash, salt, _) = hasher.HashPassword(dto.NewPassword);
            var updated = entity with { PasswordHash = hash, PasswordSalt = salt, PasswordUpdatedAt = DateTime.UtcNow };
            db.Users.Attach(updated);
            var entry = db.Entry(updated);
            entry.Property(u => u.PasswordHash).IsModified = true;
            entry.Property(u => u.PasswordSalt).IsModified = true;
            entry.Property(u => u.PasswordUpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Change current user password");

        // POST /api/users/me/avatar — upload avatar image
        group.MapPost("me/avatar", async (HttpRequest req, ClaimsPrincipal user, AppDbContext db, IObjectStorageService storage, CancellationToken ct) =>
        {
            if (!Guid.TryParse(user.FindFirst("sub")?.Value, out var userId))
                return Results.Unauthorized();

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
            var key = $"users/{userId}/avatar{ext}";
            await using var stream = file.OpenReadStream();
            var (url, storedKey) = await storage.UploadAsync(key, contentType, stream, ct);

            var entity = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (entity is null) return Results.NotFound();

            var existing = entity.Profile is null ? new JsonObject() : JsonNode.Parse(entity.Profile.RootElement.GetRawText()) as JsonObject ?? new JsonObject();
            existing["avatar"] = new JsonObject
            {
                ["url"] = url,
                ["key"] = storedKey,
                ["mime"] = contentType
            };

            using var ms = new MemoryStream();
            await using (var writer = new Utf8JsonWriter(ms))
            {
                existing.WriteTo(writer);
            }
            ms.Position = 0;
            var mergedDoc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);

            var updated = entity with { Profile = mergedDoc };
            db.Users.Attach(updated);
            db.Entry(updated).Property(u => u.Profile).IsModified = true;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { avatar = new { url, key = storedKey, mime = contentType } });
        })
        .WithSummary("Upload current user avatar");

        return app;
    }

    /// <summary>
    /// Normalizes an incoming profile patch in-place:
    /// - Trims all string values (recursively)
    /// - Validates social.* values are http(s) URLs; drops invalid entries
    /// </summary>
    private static void NormalizeProfilePatch(JsonObject node)
    {
        // Trim all string values (shallow and nested one level for common fields)
        void TrimStrings(JsonNode? n)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s):
                    var trimmed = s?.Trim();
                    if (!ReferenceEquals(trimmed, s))
                        ReplaceValue(n, trimmed ?? string.Empty);
                    break;
                case JsonObject o:
                    foreach (var k in o.ToList()) TrimStrings(o[k.Key]);
                    break;
                case JsonArray a:
                    for (int i = 0; i < a.Count; i++) TrimStrings(a[i]);
                    break;
            }
        }

        TrimStrings(node);

        // Basic social URL validation if present
        if (node["social"] is JsonObject social)
        {
            foreach (var kv in social.ToList())
            {
                if (kv.Value is JsonValue v && v.TryGetValue<string>(out var url) && !string.IsNullOrWhiteSpace(url))
                {
                    if (!IsHttpUrl(url))
                    {
                        social.Remove(kv.Key); // drop invalid URL
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns true when the string parses as an absolute http or https URL.
    /// </summary>
    private static bool IsHttpUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }
        return false;
    }

    /// <summary>
    /// Basic password strength check: at least 8 chars, must include a letter and a digit.
    /// </summary>
    private static bool IsStrongPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) return false;
        bool hasLetter = password.Any(char.IsLetter);
        bool hasDigit = password.Any(char.IsDigit);
        return hasLetter && hasDigit;
    }

    /// <summary>
    /// Deep merges patch into target:
    /// - JsonObjects are merged recursively
    /// - Arrays and primitive values from the patch replace existing values
    /// - Explicit nulls in the patch clear fields in the target
    /// Clones assigned nodes to avoid "node already has a parent" exceptions.
    /// </summary>
    // TODO (post-MVP): Consolidate this DeepMerge implementation with the identical one in
    // TenantSettingsEndpoints into a shared helper (e.g., Application/Json/JsonMerge.cs) and add
    // focused unit tests covering null-clears, array replacement, nested object creation, and
    // non-object replacement cases. (Ref: TEN-01/TEN-02 follow-up)
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
                // arrays and primitives replace; clone to avoid reusing nodes with existing parents
                target[key] = pVal!.DeepClone();
            }
        }
        return target;
    }

    /// <summary>
    /// Replaces the given JsonNode (known to be a JsonValue child of a JsonObject) with a new string value.
    /// </summary>
    private static void ReplaceValue(JsonNode node, string newValue)
    {
        // JsonNode doesn't allow directly setting value on JsonValue; recreate
        var parent = node.Parent as JsonObject;
        if (parent is not null)
        {
            var propertyName = parent.Where(kv => ReferenceEquals(kv.Value, node)).Select(kv => kv.Key).FirstOrDefault();
            if (propertyName is not null) parent[propertyName] = newValue;
        }
    }
}

internal sealed record PasswordChangeDto(string CurrentPassword, string NewPassword);
