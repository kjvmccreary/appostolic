using System.Text.Json;

namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Represents a curated denomination preset that can be applied to tenant guardrail policies.
/// Definitions capture baseline allow/deny guidance, theological notes, and escalation policies.
/// </summary>
public class GuardrailDenominationPolicy
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public JsonDocument Definition { get; set; } = null!;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
