using System.Diagnostics;

namespace Appostolic.Api.Application.Agents.Runtime;

/// <summary>
/// Central ActivitySource holders for agent runtime and tools.
/// These names must match the AddSource entries in OpenTelemetry configuration.
/// </summary>
public static class Telemetry
{
    public static readonly ActivitySource AgentSource = new("Appostolic.AgentRuntime");
    public static readonly ActivitySource ToolSource = new("Appostolic.Tools");
}
