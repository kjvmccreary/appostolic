using System.Text.Json;

namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Captures per-user guardrail preferences that are merged after tenant overrides to tailor
/// lesson guidance and safety posture to the individual while preserving tenant defaults.
/// </summary>
public class GuardrailUserPreference
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public JsonDocument Preferences { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastAppliedAt { get; set; }
}
