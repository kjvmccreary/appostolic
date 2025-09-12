using System.Text.Json;
using Appostolic.Api.Application.Agents;
using Appostolic.Api.Application.Agents.Api;
using Appostolic.Api.Application.Agents.Queue;
using Appostolic.Api.Domain.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class AgentTasksEndpoints
{
    public static IEndpointRouteBuilder MapAgentTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/agent-tasks")
            .RequireAuthorization()
            .WithTags("AgentTasks");

        // POST /api/agent-tasks
        group.MapPost("", async (
            HttpContext ctx,
            CreateAgentTaskRequest req,
            AppDbContext db,
            IAgentTaskQueue queue,
            CancellationToken ct) =>
        {
            // Validate agent
            if (req is null || req.AgentId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "agentId is required" });
            }

            var agent = AgentRegistry.FindById(req.AgentId);
            if (agent is null)
            {
                return Results.BadRequest(new { error = "invalid agentId" });
            }

            // Validate input
            if (req.Input is null || req.Input.RootElement.ValueKind == JsonValueKind.Undefined ||
                (req.Input.RootElement.ValueKind == JsonValueKind.Object && req.Input.RootElement.GetRawText() == "{}"))
            {
                return Results.BadRequest(new { error = "input cannot be empty" });
            }

            // Create task
            // Capture request context (tenant slug + user email) from dev headers for later processing
            var requestTenant = ctx.Request.Headers["x-tenant"].FirstOrDefault();
            var requestUser = ctx.Request.Headers["x-dev-user"].FirstOrDefault();

            var task = new AgentTask(Guid.NewGuid(), agent.Id, req.Input.RootElement.GetRawText())
            {
                Status = AgentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                RequestTenant = string.IsNullOrWhiteSpace(requestTenant) ? null : requestTenant.Trim(),
                RequestUser = string.IsNullOrWhiteSpace(requestUser) ? null : requestUser.Trim()
            };

            db.Add(task);
            await db.SaveChangesAsync(ct);

            // Enqueue for processing
            await queue.EnqueueAsync(task.Id, ct);

            // Metrics: task created
            Appostolic.Api.Application.Agents.Runtime.Metrics.RecordTaskCreated(task.RequestTenant, task.AgentId);

            var summary = new AgentTaskSummary(
                task.Id,
                task.AgentId,
                task.Status.ToString(),
                task.CreatedAt,
                task.StartedAt,
                task.FinishedAt,
                task.TotalTokens
            );

            return Results.Created($"/api/agent-tasks/{task.Id}", summary);
        })
        .WithSummary("Create an agent task and enqueue it for processing")
        .WithDescription("Validates the agent, persists the task (status=Pending), and enqueues it via IAgentTaskQueue. Requires dev headers in Development.");

        // GET /api/agent-tasks/{id}
        group.MapGet("{id:guid}", async (
            Guid id,
            bool? includeTraces,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var task = await db.Set<AgentTask>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null)
            {
                return Results.NotFound();
            }

            JsonDocument? resultDoc = null;
            if (!string.IsNullOrWhiteSpace(task.ResultJson))
            {
                try { resultDoc = JsonDocument.Parse(task.ResultJson!); } catch { /* ignore parse errors */ }
            }

            var details = new AgentTaskDetails(
                task.Id,
                task.AgentId,
                task.Status.ToString(),
                task.CreatedAt,
                task.StartedAt,
                task.FinishedAt,
                task.TotalPromptTokens,
                task.TotalCompletionTokens,
                task.TotalTokens,
                task.EstimatedCostUsd,
                resultDoc,
                task.ErrorMessage
            );

            if (includeTraces == true)
            {
                var traceRows = await db.Set<AgentTrace>().AsNoTracking()
                    .Where(t => t.TaskId == task.Id)
                    .OrderBy(t => t.StepNumber)
                    .ToListAsync(ct);

                var traces = new List<AgentTraceDto>(traceRows.Count);
                foreach (var tr in traceRows)
                {
                    JsonDocument inputDoc;
                    JsonDocument? outputDoc = null;
                    try { inputDoc = JsonDocument.Parse(tr.InputJson); }
                    catch { inputDoc = JsonDocument.Parse("{}\n"); }
                    try { outputDoc = JsonDocument.Parse(tr.OutputJson); } catch { /* keep null */ }

                    traces.Add(new AgentTraceDto(
                        tr.Id,
                        tr.StepNumber,
                        tr.Kind.ToString(),
                        tr.Name,
                        tr.DurationMs,
                        tr.PromptTokens,
                        tr.CompletionTokens,
                        null,
                        inputDoc,
                        outputDoc,
                        tr.CreatedAt
                    ));
                }

                return Results.Ok(new { task = details, traces });
            }

            return Results.Ok(details);
        })
        .WithSummary("Get an agent task by id")
        .WithDescription("Returns task details. Use includeTraces=true to include ordered trace steps (Model/Tool) as AgentTraceDto[].");

        // GET /api/agent-tasks (list, optional filters)
    group.MapGet("", async (
            string? status,
            Guid? agentId,
            int? take,
            int? skip,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var q = db.Set<AgentTask>().AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<AgentStatus>(status, ignoreCase: true, out var st))
                    return Results.BadRequest(new { error = "invalid status" });
                q = q.Where(t => t.Status == st);
            }

            if (agentId.HasValue && agentId.Value != Guid.Empty)
            {
                q = q.Where(t => t.AgentId == agentId.Value);
            }

            int takeVal = take.GetValueOrDefault(20);
            int skipVal = skip.GetValueOrDefault(0);
            if (takeVal <= 0) takeVal = 20;
            if (skipVal < 0) skipVal = 0;

            var items = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip(skipVal)
                .Take(takeVal)
                .Select(t => new AgentTaskSummary(
                    t.Id,
                    t.AgentId,
                    t.Status.ToString(),
                    t.CreatedAt,
                    t.StartedAt,
                    t.FinishedAt,
                    t.TotalTokens
                ))
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithSummary("List agent tasks (paged)")
        .WithDescription("Returns AgentTaskSummary[] ordered by CreatedAt DESC. Optional filters: status, agentId. Supports take/skip paging.");

    return app;
    }
}
