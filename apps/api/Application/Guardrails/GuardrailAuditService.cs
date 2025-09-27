using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appostolic.Api.Application.Storage;
using Appostolic.Api.Domain.Guardrails;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Guardrails;

/// <summary>
/// Contract for capturing guardrail audits and materializing policy snapshots.
/// </summary>
public interface IGuardrailAuditService
{
    /// <summary>
    /// Persists an audit entry for a tenant guardrail policy change and uploads the effective policy snapshot.
    /// </summary>
    Task<GuardrailPolicyAudit> RecordTenantPolicyChangeAsync(
        GuardrailTenantPolicy policy,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        string? previousPresetId,
        int? previousVersion,
        CancellationToken ct = default);

    /// <summary>
    /// Persists an audit entry for a system-level guardrail policy change.
    /// </summary>
    Task<GuardrailPolicyAudit> RecordSystemPolicyChangeAsync(
        GuardrailSystemPolicy policy,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        CancellationToken ct = default);

    /// <summary>
    /// Persists an audit entry for a denomination guardrail preset change.
    /// </summary>
    Task<GuardrailPolicyAudit> RecordPresetPolicyChangeAsync(
        GuardrailDenominationPolicy preset,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation that delegates snapshot storage to <see cref="IObjectStorageService"/> and records audit metadata.
/// </summary>
public sealed class GuardrailAuditService : IGuardrailAuditService
{
    private static readonly JsonSerializerOptions DiffSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppDbContext _db;
    private readonly IObjectStorageService _storage;
    private readonly ILogger<GuardrailAuditService> _logger;

    public GuardrailAuditService(AppDbContext db, IObjectStorageService storage, ILogger<GuardrailAuditService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GuardrailPolicyAudit> RecordTenantPolicyChangeAsync(
        GuardrailTenantPolicy policy,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        string? previousPresetId,
        int? previousVersion,
        CancellationToken ct = default)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));

        var occurredAt = DateTime.UtcNow;
        var definitionJson = policy.Definition.RootElement.GetRawText();
        var snapshotBytes = Encoding.UTF8.GetBytes(definitionJson);
        var hash = Convert.ToHexString(SHA256.HashData(snapshotBytes));

        var snapshotKey = BuildTenantSnapshotKey(policy, occurredAt);
        using var stream = new MemoryStream(snapshotBytes);
        var (url, key) = await _storage.UploadAsync(snapshotKey, "application/json", stream, ct);

        var diff = CreateDiffDocument(previousDefinition, policy.Definition, previousPresetId, policy.DerivedFromPresetId);

        var audit = new GuardrailPolicyAudit
        {
            Id = Guid.NewGuid(),
            Scope = GuardrailPolicyAuditScopes.Tenant,
            TenantId = policy.TenantId,
            TenantPolicyId = policy.Id,
            PolicyKey = policy.Key,
            Layer = policy.Layer,
            Version = policy.Version,
            PresetId = policy.DerivedFromPresetId,
            Action = action,
            ActorUserId = actorUserId,
            SnapshotKey = key,
            SnapshotUrl = url,
            SnapshotHash = hash,
            SnapshotContentType = "application/json",
            DiffSummary = diff,
            OccurredAt = occurredAt
        };

        if (previousVersion.HasValue)
        {
            audit.Version = policy.Version;
        }

        _db.GuardrailPolicyAudits.Add(audit);
        await _db.SaveChangesAsync(ct);

        previousDefinition?.Dispose();

        _logger.LogInformation("Recorded guardrail tenant audit {AuditId} for tenant {TenantId} key {Key} action {Action}", audit.Id, policy.TenantId, policy.Key, action);
        return audit;
    }

    /// <inheritdoc />
    public async Task<GuardrailPolicyAudit> RecordSystemPolicyChangeAsync(
        GuardrailSystemPolicy policy,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        CancellationToken ct = default)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));

        var occurredAt = DateTime.UtcNow;
        var definitionJson = policy.Definition.RootElement.GetRawText();
        var snapshotBytes = Encoding.UTF8.GetBytes(definitionJson);
        var hash = Convert.ToHexString(SHA256.HashData(snapshotBytes));

        var snapshotKey = BuildSystemSnapshotKey(policy, occurredAt);
        using var stream = new MemoryStream(snapshotBytes);
        var (url, key) = await _storage.UploadAsync(snapshotKey, "application/json", stream, ct);

        var diff = CreateDiffDocument(previousDefinition, policy.Definition, null, null);

        var audit = new GuardrailPolicyAudit
        {
            Id = Guid.NewGuid(),
            Scope = GuardrailPolicyAuditScopes.System,
            SystemPolicyId = policy.Id,
            PolicyKey = policy.Slug,
            Version = policy.Version,
            Action = action,
            ActorUserId = actorUserId,
            SnapshotKey = key,
            SnapshotUrl = url,
            SnapshotHash = hash,
            SnapshotContentType = "application/json",
            DiffSummary = diff,
            OccurredAt = occurredAt
        };

        _db.GuardrailPolicyAudits.Add(audit);
        await _db.SaveChangesAsync(ct);

        previousDefinition?.Dispose();

        _logger.LogInformation("Recorded guardrail system audit {AuditId} for slug {Slug} action {Action}", audit.Id, policy.Slug, action);
        return audit;
    }

    /// <inheritdoc />
    public async Task<GuardrailPolicyAudit> RecordPresetPolicyChangeAsync(
        GuardrailDenominationPolicy preset,
        string action,
        Guid? actorUserId,
        JsonDocument? previousDefinition,
        CancellationToken ct = default)
    {
        if (preset is null) throw new ArgumentNullException(nameof(preset));

        var occurredAt = DateTime.UtcNow;
        var definitionJson = preset.Definition.RootElement.GetRawText();
        var snapshotBytes = Encoding.UTF8.GetBytes(definitionJson);
        var hash = Convert.ToHexString(SHA256.HashData(snapshotBytes));

        var snapshotKey = BuildPresetSnapshotKey(preset, occurredAt);
        using var stream = new MemoryStream(snapshotBytes);
        var (url, key) = await _storage.UploadAsync(snapshotKey, "application/json", stream, ct);

        var diff = CreateDiffDocument(previousDefinition, preset.Definition, null, null);

        var audit = new GuardrailPolicyAudit
        {
            Id = Guid.NewGuid(),
            Scope = GuardrailPolicyAuditScopes.Preset,
            PresetId = preset.Id,
            PolicyKey = preset.Id,
            Version = preset.Version,
            Action = action,
            ActorUserId = actorUserId,
            SnapshotKey = key,
            SnapshotUrl = url,
            SnapshotHash = hash,
            SnapshotContentType = "application/json",
            DiffSummary = diff,
            OccurredAt = occurredAt
        };

        _db.GuardrailPolicyAudits.Add(audit);
        await _db.SaveChangesAsync(ct);

        previousDefinition?.Dispose();

        _logger.LogInformation("Recorded guardrail preset audit {AuditId} for preset {PresetId} action {Action}", audit.Id, preset.Id, action);
        return audit;
    }

    private static string BuildTenantSnapshotKey(GuardrailTenantPolicy policy, DateTime occurredAt)
    {
        var key = string.IsNullOrWhiteSpace(policy.Key) ? "default" : policy.Key.Trim().ToLowerInvariant();
        return $"guardrails/tenants/{policy.TenantId:D}/{key}/v{policy.Version:D2}-{occurredAt:yyyyMMddHHmmssfff}.json";
    }

    private static string BuildSystemSnapshotKey(GuardrailSystemPolicy policy, DateTime occurredAt)
    {
        var slug = string.IsNullOrWhiteSpace(policy.Slug) ? "system" : policy.Slug.Trim().ToLowerInvariant();
        return $"guardrails/system/{slug}/v{policy.Version:D2}-{occurredAt:yyyyMMddHHmmssfff}.json";
    }

    private static string BuildPresetSnapshotKey(GuardrailDenominationPolicy preset, DateTime occurredAt)
    {
        var id = string.IsNullOrWhiteSpace(preset.Id) ? "preset" : preset.Id.Trim().ToLowerInvariant();
        return $"guardrails/presets/{id}/v{preset.Version:D2}-{occurredAt:yyyyMMddHHmmssfff}.json";
    }

    private static JsonDocument CreateDiffDocument(JsonDocument? previous, JsonDocument current, string? previousPresetId, string? currentPresetId)
    {
        var previousAllow = ExtractArray(previous, "allow");
        var previousDeny = ExtractArray(previous, "deny");
        var previousEscalate = ExtractArray(previous, "escalate");
        var previousPresets = ExtractPresetIds(previous);

        var currentAllow = ExtractArray(current, "allow");
        var currentDeny = ExtractArray(current, "deny");
        var currentEscalate = ExtractArray(current, "escalate");
        var currentPresets = ExtractPresetIds(current);

        var payload = new
        {
            allow = BuildDiff(previousAllow, currentAllow),
            deny = BuildDiff(previousDeny, currentDeny),
            escalate = BuildDiff(previousEscalate, currentEscalate),
            presets = BuildDiff(previousPresets, currentPresets),
            derivedPreset = new
            {
                previous = previousPresetId,
                current = currentPresetId
            }
        };

        var json = JsonSerializer.Serialize(payload, DiffSerializerOptions);
        return JsonDocument.Parse(json);
    }

    private static DiffResult BuildDiff(IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
    {
        var previousSet = previous.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentSet = current.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = current.Where(x => !previousSet.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var removed = previous.Where(x => !currentSet.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var unchanged = current.Where(previousSet.Contains).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

        return new DiffResult(unchanged, added, removed);
    }

    private static IReadOnlyCollection<string> ExtractArray(JsonDocument? document, string property)
    {
        if (document is null)
        {
            return Array.Empty<string>();
        }

        if (document.RootElement.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyCollection<string> ExtractPresetIds(JsonDocument? document)
    {
        if (document is null) return Array.Empty<string>();

        var presets = new List<string>();
        if (document.RootElement.TryGetProperty("presets", out var node) && node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("denominations", out var denom) && denom.ValueKind == JsonValueKind.Array)
            {
                presets.AddRange(denom.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToLowerInvariant()));
            }
        }

        if (document.RootElement.TryGetProperty("inherits", out var inherits) && inherits.ValueKind == JsonValueKind.String)
        {
            var value = inherits.GetString();
            if (!string.IsNullOrWhiteSpace(value)) presets.Add(value.Trim().ToLowerInvariant());
        }

        return presets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record DiffResult(IReadOnlyList<string> Unchanged, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);
}
