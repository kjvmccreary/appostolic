using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Appostolic.Api.Application.Agents.Runtime;

/// <summary>
/// Central metrics instruments for agent runtime. Emitted via OpenTelemetry Meter "Appostolic.Metrics".
/// </summary>
public static class Metrics
{
    public static readonly Meter Meter = new("Appostolic.Metrics");

    // Tasks lifecycle
    public static readonly Counter<long> TasksCreated = Meter.CreateCounter<long>(
        name: "agent.tasks.created",
        unit: "{task}",
        description: "Count of agent tasks created.");

    public static readonly Counter<long> TasksCompleted = Meter.CreateCounter<long>(
        name: "agent.tasks.completed",
        unit: "{task}",
        description: "Count of agent tasks completed by status.");

    public static readonly Histogram<long> TaskDurationMs = Meter.CreateHistogram<long>(
        name: "agent.task.duration",
        unit: "ms",
        description: "End-to-end task duration from CreatedAt to FinishedAt.");

    // Model usage
    public static readonly Counter<long> ModelTokens = Meter.CreateCounter<long>(
        name: "agent.model.tokens",
        unit: "{token}",
        description: "Total tokens used by model decisions (prompt + completion)."
    );

    // Tool usage
    public static readonly Histogram<long> ToolDurationMs = Meter.CreateHistogram<long>(
        name: "agent.tool.duration",
        unit: "ms",
        description: "Duration of tool invocations.");

    public static readonly Counter<long> ToolErrors = Meter.CreateCounter<long>(
        name: "agent.tool.errors",
        unit: "{error}",
        description: "Count of tool errors by tool name.");

    public static void RecordTaskCreated(string? tenant, Guid agentId)
    {
        var tags = new TagList();
        if (!string.IsNullOrWhiteSpace(tenant)) tags.Add("tenant", tenant);
        if (agentId != Guid.Empty) tags.Add("agent.id", agentId);
        TasksCreated.Add(1, tags);
    }

    public static void RecordTaskCompleted(Appostolic.Api.Domain.Agents.AgentTask task)
    {
        var tags = new TagList
        {
            { "status", task.Status.ToString() }
        };
        TasksCompleted.Add(1, tags);

        if (task.FinishedAt.HasValue)
        {
            var ms = (long)Math.Max(0, (task.FinishedAt.Value - task.CreatedAt).TotalMilliseconds);
            TaskDurationMs.Record(ms, tags);
        }
    }

    public static void RecordToolDuration(string toolName, long ms)
    {
        var tags = new TagList { { "tool", toolName } };
        ToolDurationMs.Record(ms, tags);
    }

    public static void IncrementToolError(string toolName)
    {
        var tags = new TagList { { "tool", toolName } };
        ToolErrors.Add(1, tags);
    }
}
