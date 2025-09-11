using System.Text.Json;
using Appostolic.Api.Domain.Agents;

namespace Appostolic.Api.Application.Agents.Runtime;

public interface ITraceWriter
{
    Task<Guid> WriteModelStepAsync(AgentTask task, int step, Model.ModelPrompt prompt, Model.ModelDecision decision, int durationMs, CancellationToken ct);
    Task<Guid> WriteToolStepAsync(AgentTask task, int step, string toolName, JsonDocument input, JsonDocument? output, bool success, int durationMs, int? promptTokens = null, int? completionTokens = null, string? error = null, CancellationToken ct = default);
}

public sealed class TraceWriter : ITraceWriter
{
    private readonly AppDbContext _db;

    public TraceWriter(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> WriteModelStepAsync(AgentTask task, int step, Model.ModelPrompt prompt, Model.ModelDecision decision, int durationMs, CancellationToken ct)
    {
        var trace = new AgentTrace(
            id: Guid.NewGuid(),
            taskId: task.Id,
            stepNumber: step,
            kind: TraceKind.Model,
            name: "model",
            inputJson: JsonSerializer.Serialize(new
            {
                system = prompt.SystemPrompt,
                context = prompt.Context?.RootElement
            }),
            outputJson: JsonSerializer.Serialize(new
            {
                action = decision.Action.GetType().Name,
                decision.PromptTokens,
                decision.CompletionTokens,
                decision.Rationale
            })
        )
        {
            DurationMs = durationMs,
            PromptTokens = decision.PromptTokens,
            CompletionTokens = decision.CompletionTokens
        };

        _db.AgentTraces.Add(trace);
        await _db.SaveChangesAsync(ct);
        return trace.Id;
    }

    public async Task<Guid> WriteToolStepAsync(AgentTask task, int step, string toolName, JsonDocument input, JsonDocument? output, bool success, int durationMs, int? promptTokens = null, int? completionTokens = null, string? error = null, CancellationToken ct = default)
    {
        var trace = new AgentTrace(
            id: Guid.NewGuid(),
            taskId: task.Id,
            stepNumber: step,
            kind: TraceKind.Tool,
            name: toolName,
            inputJson: SerializeJson(input),
            outputJson: SerializeJson(output) ?? (success ? "{}" : JsonSerializer.Serialize(new { error = error ?? "unknown error" }))
        )
        {
            DurationMs = durationMs,
            PromptTokens = promptTokens ?? 0,
            CompletionTokens = completionTokens ?? 0
        };

        _db.AgentTraces.Add(trace);
        await _db.SaveChangesAsync(ct);
        return trace.Id;
    }

    private static string SerializeJson(JsonDocument? doc)
    {
        if (doc is null) return null!;
        return doc.RootElement.GetRawText();
    }
}
