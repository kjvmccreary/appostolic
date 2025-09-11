using System.Text.Json;
using Appostolic.Api.Domain.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Appostolic.Api.Application.Agents.Runtime;

public interface ITraceWriter
{
    Task<Guid> WriteModelStepAsync(AgentTask task, int step, Model.ModelPrompt prompt, Model.ModelDecision decision, int durationMs, CancellationToken ct);
    Task<Guid> WriteToolStepAsync(AgentTask task, int step, string toolName, JsonDocument input, JsonDocument? output, bool success, int durationMs, int? promptTokens = null, int? completionTokens = null, string? error = null, CancellationToken ct = default);
}

public sealed class TraceWriter : ITraceWriter
{
    private readonly AppDbContext _db;
    private readonly ILogger<TraceWriter> _logger;

    public TraceWriter(AppDbContext db, ILogger<TraceWriter> logger)
    {
        _db = db;
        _logger = logger;
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
            DurationMs = Math.Max(0, durationMs),
            PromptTokens = Math.Max(0, decision.PromptTokens),
            CompletionTokens = Math.Max(0, decision.CompletionTokens)
        };

        _db.AgentTraces.Add(trace);
        await SaveWithRetryAsync(trace, step, ct);
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
            DurationMs = Math.Max(0, durationMs),
            PromptTokens = Math.Max(0, promptTokens ?? 0),
            CompletionTokens = Math.Max(0, completionTokens ?? 0)
        };

        _db.AgentTraces.Add(trace);
        await SaveWithRetryAsync(trace, step, ct);
        return trace.Id;
    }

    private static string SerializeJson(JsonDocument? doc)
    {
        if (doc is null) return null!;
        return doc.RootElement.GetRawText();
    }

    private async Task SaveWithRetryAsync(AgentTrace trace, int originalStep, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Retry once with StepNumber++
            trace.StepNumber = originalStep + 1;
            _logger.LogWarning("Unique index conflict on (TaskId, StepNumber) for task {TaskId} step {Step}. Retrying once with step {NewStep}.", trace.TaskId, originalStep, trace.StepNumber);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            return true;
        return false;
    }
}
