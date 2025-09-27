using System.Text.Json;

namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Represents the global system-level guardrail policy. This layer always participates in evaluation
/// prior to denomination or tenant-specific layers and encodes immutable doctrinal baselines.
/// </summary>
public class GuardrailSystemPolicy
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonDocument Definition { get; set; } = null!;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
