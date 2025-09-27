using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Appostolic.Api.Application.Guardrails;
using Appostolic.Api.Domain.Guardrails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

/// <summary>
/// Administrative guardrail endpoints enabling tenant admins to inspect and manage policy layers.
/// </summary>
public static class GuardrailAdminEndpoints
{
    public static IEndpointRouteBuilder MapGuardrailAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var tenantGroup = app.MapGroup("/api/guardrails/admin")
            .RequireAuthorization("TenantAdmin")
            .WithTags("GuardrailsAdmin");

        tenantGroup.MapGet("tenant", HandleGetTenantPolicies)
            .WithSummary("List guardrail policies for the current tenant (default key unless specified)")
            .WithDescription("Returns tenant policy layers, available presets, and an evaluation snapshot for administrative review.");

        tenantGroup.MapPut("tenant/{policyKey}/draft", HandleUpsertDraft)
            .WithSummary("Create or update a tenant guardrail draft policy")
            .WithDescription("Upserts a draft policy definition for the provided key. Drafts remain inactive until published.");

        tenantGroup.MapPost("tenant/{policyKey}/publish", HandlePublishDraft)
            .WithSummary("Publish the latest guardrail draft as the active tenant policy")
            .WithDescription("Promotes the current draft definition into the active tenant policy layer, incrementing the version.");

        tenantGroup.MapPost("tenant/{policyKey}/reset", HandleResetToPreset)
            .WithSummary("Reset the active tenant guardrail policy to a denomination preset")
            .WithDescription("Replaces the tenant policy definition with the specified preset and clears existing drafts.");

        var superGroup = app.MapGroup("/api/guardrails/admin/super")
            .RequireAuthorization()
            .WithTags("GuardrailsAdmin");

        superGroup.MapGet("state", HandleGetSuperadminState)
            .WithSummary("View guardrail system presets and recent tenant activity")
            .WithDescription("Returns system-level policies, denomination presets, and recent tenant guardrail changes for platform operators.");

        superGroup.MapPut("system/{slug}", HandleUpsertSystemPolicy)
            .WithSummary("Create or update a system guardrail policy")
            .WithDescription("Upserts a system (global) guardrail policy definition identified by slug. Increments version on update.");

        superGroup.MapPut("presets/{presetId}", HandleUpsertPreset)
            .WithSummary("Create or update a denomination guardrail preset")
            .WithDescription("Upserts a denomination preset definition accessible to tenants. Increments version when updating existing presets.");

        return app;
    }

    private static async Task<IResult> HandleGetTenantPolicies(
        ClaimsPrincipal principal,
        AppDbContext db,
        IGuardrailEvaluator evaluator,
        string? policyKey,
        CancellationToken ct)
    {
        if (!TryResolveTenant(principal, out var tenantId))
        {
            return Results.BadRequest(new { error = "missing_tenant_scope" });
        }

        var key = NormalizeKey(policyKey);

        var policies = await db.GuardrailTenantPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Key == key)
            .OrderBy(p => p.Layer)
            .ThenByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .ToListAsync(ct);

        var presets = await db.GuardrailDenominationPolicies.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new GuardrailPresetSummaryDto(p.Id, p.Name, p.Notes, p.Version))
            .ToListAsync(ct);

        // Evaluate snapshot for current tenant/base policy
        var evaluation = await evaluator.EvaluateAsync(new GuardrailEvaluationContext
        {
            TenantId = tenantId,
            PolicyKey = key,
            Signals = Array.Empty<string>()
        }, ct);

        var response = new TenantGuardrailSummaryDto(
            key,
            policies.Select(MapPolicy).ToList(),
            GuardrailResponseFactory.CreateResponse(evaluation),
            presets);

        return Results.Ok(response);
    }

    private static async Task<IResult> HandleUpsertDraft(
        ClaimsPrincipal principal,
        AppDbContext db,
        string policyKey,
        UpsertTenantGuardrailDraftRequest dto,
        CancellationToken ct)
    {
        if (!TryResolveTenant(principal, out var tenantId))
        {
            return Results.BadRequest(new { error = "missing_tenant_scope" });
        }
        if (!TryResolveUser(principal, out var userId))
        {
            return Results.Unauthorized();
        }
        if (dto.Definition is null || dto.Definition.RootElement.ValueKind == JsonValueKind.Null)
        {
            return Results.BadRequest(new { error = "definition_required" });
        }

        var key = NormalizeKey(policyKey);
        var now = DateTime.UtcNow;

        var draft = await db.GuardrailTenantPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Key == key && p.Layer == GuardrailPolicyLayer.Draft, ct);

        var definition = CloneJson(dto.Definition);
        var metadata = dto.Metadata is null ? null : CloneJson(dto.Metadata);

        if (draft is null)
        {
            draft = new GuardrailTenantPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = key,
                Layer = GuardrailPolicyLayer.Draft,
                Definition = definition,
                Metadata = metadata,
                DerivedFromPresetId = NormalizeOptional(dto.DerivedFromPresetId),
                CreatedAt = now,
                CreatedByUserId = userId,
                UpdatedAt = now,
                UpdatedByUserId = userId,
                IsActive = false
            };
            db.GuardrailTenantPolicies.Add(draft);
        }
        else
        {
            draft.Definition = definition;
            draft.Metadata = metadata;
            draft.DerivedFromPresetId = NormalizeOptional(dto.DerivedFromPresetId);
            draft.UpdatedAt = now;
            draft.UpdatedByUserId = userId;
            draft.IsActive = false;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(MapPolicy(draft));
    }

    private static async Task<IResult> HandlePublishDraft(
        ClaimsPrincipal principal,
        AppDbContext db,
        string policyKey,
        CancellationToken ct)
    {
        if (!TryResolveTenant(principal, out var tenantId))
        {
            return Results.BadRequest(new { error = "missing_tenant_scope" });
        }
        if (!TryResolveUser(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var key = NormalizeKey(policyKey);
        var draft = await db.GuardrailTenantPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Key == key && p.Layer == GuardrailPolicyLayer.Draft, ct);
        if (draft is null)
        {
            return Results.BadRequest(new { error = "draft_not_found" });
        }

        var basePolicy = await db.GuardrailTenantPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Key == key && p.Layer == GuardrailPolicyLayer.TenantBase && p.IsActive, ct);

        var now = DateTime.UtcNow;
        var clonedDefinition = CloneJson(draft.Definition);
        var clonedMetadata = draft.Metadata is null ? null : CloneJson(draft.Metadata);

        if (basePolicy is null)
        {
            basePolicy = new GuardrailTenantPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = key,
                Layer = GuardrailPolicyLayer.TenantBase,
                Definition = clonedDefinition,
                Metadata = clonedMetadata,
                DerivedFromPresetId = draft.DerivedFromPresetId,
                CreatedAt = now,
                CreatedByUserId = userId,
                UpdatedAt = now,
                UpdatedByUserId = userId,
                PublishedAt = now,
                IsActive = true
            };
            db.GuardrailTenantPolicies.Add(basePolicy);
        }
        else
        {
            basePolicy.Definition = clonedDefinition;
            basePolicy.Metadata = clonedMetadata;
            basePolicy.DerivedFromPresetId = draft.DerivedFromPresetId;
            basePolicy.UpdatedAt = now;
            basePolicy.UpdatedByUserId = userId;
            basePolicy.PublishedAt = now;
            basePolicy.Version += 1;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapPolicy(basePolicy));
    }

    private static async Task<IResult> HandleResetToPreset(
        ClaimsPrincipal principal,
        AppDbContext db,
        string policyKey,
        ResetTenantGuardrailRequest dto,
        CancellationToken ct)
    {
        if (!TryResolveTenant(principal, out var tenantId))
        {
            return Results.BadRequest(new { error = "missing_tenant_scope" });
        }
        if (!TryResolveUser(principal, out var userId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(dto.PresetId))
        {
            return Results.BadRequest(new { error = "preset_required" });
        }

        var preset = await db.GuardrailDenominationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dto.PresetId, ct);
        if (preset is null)
        {
            return Results.NotFound(new { error = "preset_not_found" });
        }

        var key = NormalizeKey(policyKey);
        var now = DateTime.UtcNow;

        var basePolicy = await db.GuardrailTenantPolicies
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Key == key && p.Layer == GuardrailPolicyLayer.TenantBase && p.IsActive, ct);

        var definition = CloneJson(preset.Definition);

        if (basePolicy is null)
        {
            basePolicy = new GuardrailTenantPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = key,
                Layer = GuardrailPolicyLayer.TenantBase,
                Definition = definition,
                Metadata = null,
                DerivedFromPresetId = preset.Id,
                CreatedAt = now,
                CreatedByUserId = userId,
                UpdatedAt = now,
                UpdatedByUserId = userId,
                PublishedAt = now,
                IsActive = true
            };
            db.GuardrailTenantPolicies.Add(basePolicy);
        }
        else
        {
            basePolicy.Definition = definition;
            basePolicy.Metadata = null;
            basePolicy.DerivedFromPresetId = preset.Id;
            basePolicy.UpdatedAt = now;
            basePolicy.UpdatedByUserId = userId;
            basePolicy.PublishedAt = now;
            basePolicy.Version += 1;
        }

        // Remove existing drafts for this key to avoid stale content lingering post-reset.
        var drafts = await db.GuardrailTenantPolicies
            .Where(p => p.TenantId == tenantId && p.Key == key && p.Layer == GuardrailPolicyLayer.Draft)
            .ToListAsync(ct);
        if (drafts.Count > 0)
        {
            db.GuardrailTenantPolicies.RemoveRange(drafts);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapPolicy(basePolicy));
    }

    /// <summary>
    /// Retrieves aggregated guardrail state (system policies, presets, activity) for platform operators.
    /// </summary>
    private static async Task<IResult> HandleGetSuperadminState(
        ClaimsPrincipal principal,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var systemPolicies = await db.GuardrailSystemPolicies.AsNoTracking()
            .OrderBy(p => p.Slug)
            .Select(p => MapSystemPolicy(p))
            .ToListAsync(ct);

        var presets = await db.GuardrailDenominationPolicies.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => MapPreset(p))
            .ToListAsync(ct);

        var activityRows = await (
                from policy in db.GuardrailTenantPolicies.AsNoTracking()
                join tenant in db.Tenants.AsNoTracking() on policy.TenantId equals tenant.Id into tenantJoin
                from tenant in tenantJoin.DefaultIfEmpty()
                join updatedUser in db.Users.AsNoTracking() on policy.UpdatedByUserId equals updatedUser.Id into userJoin
                from updatedUser in userJoin.DefaultIfEmpty()
                let occurredAt = policy.UpdatedAt ?? policy.PublishedAt ?? policy.CreatedAt
                orderby occurredAt descending
                select new
                {
                    policy.Id,
                    policy.TenantId,
                    TenantName = tenant != null ? tenant.Name : null,
                    policy.Key,
                    policy.Layer,
                    policy.Version,
                    UpdatedByEmail = updatedUser != null ? updatedUser.Email : null,
                    OccurredAt = occurredAt,
                    policy.DerivedFromPresetId,
                    policy.IsActive,
                    policy.PublishedAt,
                    policy.UpdatedAt
                }
            )
            .Take(50)
            .ToListAsync(ct);

        var activity = activityRows
            .Select(row => new GuardrailActivityEntryDto(
                row.Id,
                row.TenantId,
                row.TenantName,
                row.Key,
                row.Layer.ToString().ToLowerInvariant(),
                row.Version,
                row.UpdatedByEmail,
                DetermineActivityKind(row.Layer, row.PublishedAt, row.UpdatedAt, row.DerivedFromPresetId),
                row.OccurredAt,
                row.DerivedFromPresetId,
                row.IsActive,
                row.PublishedAt))
            .ToList();

        var response = new GuardrailSuperadminSummaryDto(systemPolicies, presets, activity);
        return Results.Ok(response);
    }

    /// <summary>
    /// Creates or updates a system-level guardrail policy identified by slug.
    /// </summary>
    private static async Task<IResult> HandleUpsertSystemPolicy(
        ClaimsPrincipal principal,
        AppDbContext db,
        string slug,
        UpsertSystemGuardrailRequest dto,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        if (dto.Definition is null || dto.Definition.RootElement.ValueKind == JsonValueKind.Null)
        {
            return Results.BadRequest(new { error = "definition_required" });
        }

        var normalizedSlug = NormalizeKey(slug);
        var now = DateTime.UtcNow;

        var policy = await db.GuardrailSystemPolicies
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug, ct);

        var definition = CloneJson(dto.Definition);

        if (policy is null)
        {
            policy = new GuardrailSystemPolicy
            {
                Id = Guid.NewGuid(),
                Slug = normalizedSlug,
                Name = dto.Name?.Trim() ?? normalizedSlug,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                Definition = definition,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.GuardrailSystemPolicies.Add(policy);
        }
        else
        {
            policy.Name = dto.Name?.Trim() ?? policy.Name;
            policy.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            policy.Definition = definition;
            policy.Version += 1;
            policy.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapSystemPolicy(policy));
    }

    /// <summary>
    /// Creates or updates a denomination preset definition accessible to tenant admins.
    /// </summary>
    private static async Task<IResult> HandleUpsertPreset(
        ClaimsPrincipal principal,
        AppDbContext db,
        string presetId,
        UpsertDenominationGuardrailRequest dto,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(principal))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        if (dto.Definition is null || dto.Definition.RootElement.ValueKind == JsonValueKind.Null)
        {
            return Results.BadRequest(new { error = "definition_required" });
        }

        var normalizedId = presetId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return Results.BadRequest(new { error = "preset_id_required" });
        }

        var now = DateTime.UtcNow;
        var preset = await db.GuardrailDenominationPolicies
            .FirstOrDefaultAsync(p => p.Id == normalizedId, ct);

        var definition = CloneJson(dto.Definition);

        if (preset is null)
        {
            preset = new GuardrailDenominationPolicy
            {
                Id = normalizedId,
                Name = dto.Name?.Trim() ?? normalizedId,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
                Definition = definition,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.GuardrailDenominationPolicies.Add(preset);
        }
        else
        {
            preset.Name = dto.Name?.Trim() ?? preset.Name;
            preset.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            preset.Definition = definition;
            preset.Version += 1;
            preset.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(MapPreset(preset));
    }

    private static bool TryResolveTenant(ClaimsPrincipal principal, out Guid tenantId)
    {
        tenantId = default;
        var claim = principal.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(claim, out tenantId);
    }

    private static bool TryResolveUser(ClaimsPrincipal principal, out Guid userId)
    {
        userId = default;
        var userClaim = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userClaim, out userId);
    }

    private static string NormalizeKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? "default" : key.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsSuperAdmin(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("superadmin")?.Value;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Clones the provided JSON document to decouple from incoming request lifetime.
    /// </summary>
    private static JsonDocument CloneJson(JsonDocument source)
    {
        return JsonDocument.Parse(source.RootElement.GetRawText());
    }

    private static GuardrailSystemPolicyDto MapSystemPolicy(GuardrailSystemPolicy policy)
    {
        var definition = JsonNode.Parse(policy.Definition.RootElement.GetRawText())!;
        return new GuardrailSystemPolicyDto(
            policy.Id,
            policy.Slug,
            policy.Name,
            policy.Description,
            policy.Version,
            policy.CreatedAt,
            policy.UpdatedAt,
            definition);
    }

    private static GuardrailDenominationPolicyDto MapPreset(GuardrailDenominationPolicy preset)
    {
        var definition = JsonNode.Parse(preset.Definition.RootElement.GetRawText())!;
        return new GuardrailDenominationPolicyDto(
            preset.Id,
            preset.Name,
            preset.Notes,
            preset.Version,
            preset.CreatedAt,
            preset.UpdatedAt,
            definition);
    }

    private static string DetermineActivityKind(GuardrailPolicyLayer layer, DateTime? publishedAt, DateTime? updatedAt, string? derivedFromPresetId)
    {
        if (layer == GuardrailPolicyLayer.Draft)
        {
            return "draft_saved";
        }

        if (publishedAt.HasValue && (!updatedAt.HasValue || publishedAt.Value == updatedAt.Value))
        {
            return derivedFromPresetId is not null ? "preset_applied" : "published";
        }

        if (publishedAt.HasValue)
        {
            return "published";
        }

        return "updated";
    }

    private static TenantGuardrailPolicyDto MapPolicy(GuardrailTenantPolicy policy)
    {
        JsonNode? metadata = null;
        if (policy.Metadata is not null)
        {
            metadata = JsonNode.Parse(policy.Metadata.RootElement.GetRawText());
        }

        var definition = JsonNode.Parse(policy.Definition?.RootElement.GetRawText() ?? "{}")!;

        return new TenantGuardrailPolicyDto(
            policy.Id,
            policy.Key,
            policy.Layer.ToString().ToLowerInvariant(),
            policy.Version,
            policy.IsActive,
            policy.DerivedFromPresetId,
            policy.CreatedByUserId,
            policy.UpdatedByUserId,
            policy.CreatedAt,
            policy.UpdatedAt,
            policy.PublishedAt,
            metadata,
            definition);
    }
}

/// <summary>
/// Request body for upserting a draft tenant guardrail policy.
/// </summary>
public sealed record UpsertTenantGuardrailDraftRequest(JsonDocument Definition, string? DerivedFromPresetId, JsonDocument? Metadata);

/// <summary>
/// Request body for resetting a tenant guardrail policy to a preset.
/// </summary>
public sealed record ResetTenantGuardrailRequest(string PresetId);

/// <summary>
/// Request body for upserting a system-level guardrail policy.
/// </summary>
public sealed record UpsertSystemGuardrailRequest(string? Name, string? Description, JsonDocument Definition);

/// <summary>
/// Request body for upserting a denomination preset definition.
/// </summary>
public sealed record UpsertDenominationGuardrailRequest(string? Name, string? Notes, JsonDocument Definition);

/// <summary>
/// DTO representing a tenant guardrail policy layer for administration.
/// </summary>
public sealed record TenantGuardrailPolicyDto(
    Guid Id,
    string Key,
    string Layer,
    int Version,
    bool IsActive,
    string? DerivedFromPresetId,
    Guid? CreatedByUserId,
    Guid? UpdatedByUserId,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? PublishedAt,
    JsonNode? Metadata,
    JsonNode Definition);

/// <summary>
/// Summary response for tenant guardrail administration views.
/// </summary>
public sealed record TenantGuardrailSummaryDto(
    string Key,
    IReadOnlyList<TenantGuardrailPolicyDto> Policies,
    GuardrailPreflightResponseDto Snapshot,
    IReadOnlyList<GuardrailPresetSummaryDto> Presets);

/// <summary>
/// Summary of available denomination presets.
/// </summary>
public sealed record GuardrailPresetSummaryDto(string Id, string Name, string? Notes, int Version);

/// <summary>
/// DTO representing a system guardrail policy for superadmin tooling.
/// </summary>
public sealed record GuardrailSystemPolicyDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    int Version,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    JsonNode Definition);

/// <summary>
/// DTO representing a denomination guardrail preset with definition payload.
/// </summary>
public sealed record GuardrailDenominationPolicyDto(
    string Id,
    string Name,
    string? Notes,
    int Version,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    JsonNode Definition);

/// <summary>
/// Audit-oriented snapshot of recent tenant guardrail activity.
/// </summary>
public sealed record GuardrailActivityEntryDto(
    Guid PolicyId,
    Guid TenantId,
    string? TenantName,
    string Key,
    string Layer,
    int Version,
    string? UpdatedByEmail,
    string Action,
    DateTime OccurredAt,
    string? DerivedFromPresetId,
    bool IsActive,
    DateTime? PublishedAt);

/// <summary>
/// Aggregated response used by the platform superadmin console.
/// </summary>
public sealed record GuardrailSuperadminSummaryDto(
    IReadOnlyList<GuardrailSystemPolicyDto> SystemPolicies,
    IReadOnlyList<GuardrailDenominationPolicyDto> Presets,
    IReadOnlyList<GuardrailActivityEntryDto> Activity);
