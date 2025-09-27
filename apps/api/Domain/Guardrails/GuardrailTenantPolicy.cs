using System.Text.Json;

namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Represents a tenant-scoped guardrail definition. Policies may be layered (base, override, draft)
/// and keyed to support targeted subsets such as ministry contexts or delivery channels.
/// </summary>
public class GuardrailTenantPolicy
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public GuardrailPolicyLayer Layer { get; set; } = GuardrailPolicyLayer.TenantBase;
    public string Key { get; set; } = "default";
    public JsonDocument Definition { get; set; } = null!;
    public string? DerivedFromPresetId { get; set; }
    public JsonDocument? Metadata { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}
