using System.Text.Json;

namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Represents a persisted audit entry for guardrail policy changes with an accompanying object storage snapshot.
/// </summary>
public class GuardrailPolicyAudit
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = GuardrailPolicyAuditScopes.Tenant;
    public Guid? TenantId { get; set; }
    public Guid? TenantPolicyId { get; set; }
    public Guid? SystemPolicyId { get; set; }
    public string? PresetId { get; set; }
    public string? PolicyKey { get; set; }
    public GuardrailPolicyLayer? Layer { get; set; }
    public int? Version { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string SnapshotKey { get; set; } = string.Empty;
    public string SnapshotUrl { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public string SnapshotContentType { get; set; } = "application/json";
    public JsonDocument? DiffSummary { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Well-known scope identifiers describing the guardrail domain that produced an audit.
/// </summary>
public static class GuardrailPolicyAuditScopes
{
    public const string Tenant = "tenant";
    public const string System = "system";
    public const string Preset = "preset";
}

/// <summary>
/// Canonical action identifiers stored with guardrail policy audits for downstream consumption.
/// </summary>
public static class GuardrailPolicyAuditActions
{
    public const string TenantPublish = "tenant_publish";
    public const string TenantReset = "tenant_reset";
    public const string SystemUpsert = "system_upsert";
    public const string PresetUpsert = "preset_upsert";
}
