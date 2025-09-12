using System.Text.Json;
using Appostolic.Api.Domain.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class AgentTasksExportEndpoints
{
    public static IEndpointRouteBuilder MapAgentTasksExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/agent-tasks")
            .RequireAuthorization()
            .WithTags("AgentTasks");

        group.MapGet("{id:guid}/export", async (Guid id, AppDbContext db, HttpContext ctx, CancellationToken ct) =>
        {
            var task = await db.Set<AgentTask>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null)
            {
                return Results.NotFound();
            }

            var traces = await db.Set<AgentTrace>().AsNoTracking()
                .Where(t => t.TaskId == id)
                .OrderBy(t => t.StepNumber)
                .Select(t => new
                {
                    t.Id,
                    t.StepNumber,
                    Kind = t.Kind.ToString(),
                    t.Name,
                    t.DurationMs,
                    t.PromptTokens,
                    t.CompletionTokens,
                    t.CreatedAt,
                    Input = SafeParse(t.InputJson),
                    Output = SafeParseNullable(t.OutputJson)
                })
                .ToListAsync(ct);

            var resultObj = SafeParseNullable(task.ResultJson);

            var payload = new
            {
                task = new
                {
                    task.Id,
                    task.AgentId,
                    Status = task.Status.ToString(),
                    task.CreatedAt,
                    task.StartedAt,
                    task.FinishedAt,
                    task.TotalPromptTokens,
                    task.TotalCompletionTokens,
                    TotalTokens = task.TotalTokens,
                    task.EstimatedCostUsd,
                    task.RequestTenant,
                    task.RequestUser,
                    Result = resultObj,
                    task.ErrorMessage
                },
                traces
            };

            var fileName = $"agent-task-{task.Id}.json";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";
            return Results.Json(payload, contentType: "application/json");
        })
        .WithSummary("Export task and traces as JSON")
        .WithDescription("Returns { task, traces[] } with result, totals, cost, and ordered traces; suggests attachment filename.");

        return app;
    }

    private static JsonDocument SafeParse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch { return JsonDocument.Parse("{}\n"); }
    }

    private static JsonDocument? SafeParseNullable(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }
}
