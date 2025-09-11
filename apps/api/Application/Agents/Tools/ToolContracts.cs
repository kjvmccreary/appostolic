using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Tool call request contract. <see cref="Name"/> must match the tool being invoked.
/// </summary>
/// <param name="Name">Tool name (should equal the tool's <see cref="ITool.Name"/>).</param>
/// <param name="Input">Raw JSON input payload for the tool.</param>
public readonly record struct ToolCallRequest(string Name, JsonDocument Input)
{
    /// <summary>
    /// Parse the input JSON to a strongly-typed payload using System.Text.Json.
    /// Throws <see cref="JsonException"/> if deserialization fails.
    /// </summary>
    public T ReadInput<T>()
    {
        if (Input is null) throw new ArgumentException("Input is required", nameof(Input));
        return Input.Deserialize<T>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        })!;
    }
}

/// <summary>
/// Tool call result encapsulating success, optional output, error details, and duration.
/// </summary>
/// <param name="Success">True if the tool succeeded.</param>
/// <param name="Output">Optional JSON output when successful.</param>
/// <param name="Error">Optional error message when failed.</param>
/// <param name="DurationMs">Execution time in milliseconds.</param>
public readonly record struct ToolCallResult(bool Success, JsonDocument? Output, string? Error, int DurationMs);

/// <summary>
/// Ambient execution context supplied to tools.
/// </summary>
/// <param name="TaskId">AgentTask identifier.</param>
/// <param name="StepNumber">Current step sequence number.</param>
/// <param name="Tenant">Tenant identifier or slug.</param>
/// <param name="User">User identifier (subject/email).</param>
/// <param name="Logger">ILogger instance for structured logging.</param>
/// <param name="Activity">Optional Activity for tracing.</param>
public readonly record struct ToolExecutionContext(Guid TaskId, int StepNumber, string Tenant, string User, ILogger Logger, Activity? Activity = null);
