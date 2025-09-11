using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Represents an executable tool the agent runtime can call, such as
/// "web.search", "db.query", or "fs.write".
/// </summary>
public interface ITool
{
    /// <summary>
    /// Canonical tool name (case-insensitive lookup), e.g., "web.search".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Execute the tool with the provided input and execution context.
    /// </summary>
    Task<ToolCallResult> InvokeAsync(ToolCallRequest request, ToolExecutionContext ctx, CancellationToken ct);
}
