namespace Appostolic.Api.Application.Agents.Model;

/// <summary>
/// Deterministic, heuristic token estimator. No external dependencies.
/// Assumes roughly 4 ASCII-ish characters per token.
/// </summary>
public static class TokenEstimator
{
    /// <summary>
    /// Estimate token count for the given text and model using a simple 4 chars/token heuristic.
    /// </summary>
    /// <param name="text">Input text.</param>
    /// <param name="model">Model name (currently unused; kept for future model-specific tuning).</param>
    public static int EstimateTokens(string text, string model)
        => (int)System.Math.Ceiling(((text?.Length) ?? 0) / 4.0);
}
