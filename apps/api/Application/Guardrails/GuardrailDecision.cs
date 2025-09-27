namespace Appostolic.Api.Application.Guardrails;

/// <summary>
/// Possible decision outcomes returned by the guardrail evaluator when assessing a request.
/// </summary>
public enum GuardrailDecision
{
    Allow = 0,
    Escalate = 1,
    Deny = 2
}
