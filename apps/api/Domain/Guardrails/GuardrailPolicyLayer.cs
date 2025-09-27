namespace Appostolic.Api.Domain.Guardrails;

/// <summary>
/// Indicates the tenant-level policy layer applied when merging guardrail definitions.
/// Base layers represent the canonical tenant policy, while overrides capture targeted adjustments
/// and drafts capture unpublished edits.
/// </summary>
public enum GuardrailPolicyLayer
{
    TenantBase = 0,
    Override = 1,
    Draft = 2
}
