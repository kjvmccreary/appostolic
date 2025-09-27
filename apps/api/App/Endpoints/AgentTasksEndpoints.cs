using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Appostolic.Api.Application.Agents;
using Appostolic.Api.Application.Agents.Api;
using Appostolic.Api.Application.Agents.Queue;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.Application.Guardrails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

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
            IGuardrailEvaluator guardrailEvaluator,
            IGuardrailSecurityEventWriter securityEvents,
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

            GuardrailEvaluationResult? evaluationResult = null;
            GuardrailDecision? guardrailDecision = null;
            string? guardrailMetadataJson = null;

            if (req.Guardrails is not null)
            {
                if (!Guid.TryParse(ctx.User.FindFirst("tenant_id")?.Value, out var tenantId))
                {
                    return Results.BadRequest(new { error = "missing_tenant_scope" });
                }

                var userIdClaim = ctx.User.FindFirst("sub")?.Value
                                 ?? ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(userIdClaim, out var authenticatedUserId))
                {
                    return Results.Unauthorized();
                }

                var requestedUserId = req.Guardrails.UserId;
                var targetUserId = requestedUserId ?? authenticatedUserId;
                if (requestedUserId.HasValue && requestedUserId.Value != authenticatedUserId)
                {
                    return Results.Forbid();
                }

                var policyKey = string.IsNullOrWhiteSpace(req.Guardrails.PolicyKey)
                    ? "default"
                    : req.Guardrails.PolicyKey.Trim().ToLowerInvariant();

                var signals = (req.Guardrails.Signals ?? Array.Empty<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .ToList();

                var evaluationContext = new GuardrailEvaluationContext
                {
                    TenantId = tenantId,
                    UserId = targetUserId,
                    PolicyKey = policyKey,
                    Signals = signals,
                    Channel = req.Guardrails.Channel,
                    PromptSummary = req.Guardrails.PromptSummary,
                    PresetIds = req.Guardrails.PresetIds
                };

                evaluationResult = await guardrailEvaluator.EvaluateAsync(evaluationContext, ct);
                guardrailDecision = evaluationResult.Decision;

                if (guardrailDecision is GuardrailDecision.Deny or GuardrailDecision.Escalate)
                {
                    GuardrailResponseFactory.EmitSecurityEvent(
                        securityEvents,
                        tenantId,
                        targetUserId,
                        req.Guardrails.Channel,
                        req.Guardrails.PromptSummary,
                        evaluationResult);
                }

                var metadataOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var decisionString = JsonNamingPolicy.CamelCase.ConvertName(evaluationResult.Decision.ToString());
                guardrailMetadataJson = JsonSerializer.Serialize(new
                {
                    evaluatedAt = DateTime.UtcNow,
                    context = new
                    {
                        tenantId,
                        policyKey,
                        channel = req.Guardrails.Channel,
                        promptSummary = req.Guardrails.PromptSummary,
                        signals,
                        presetIds = req.Guardrails.PresetIds,
                        requestedUserId,
                        evaluatedUserId = targetUserId
                    },
                    result = new
                    {
                        decision = decisionString,
                        reasonCode = evaluationResult.ReasonCode,
                        matchedSignals = evaluationResult.MatchedSignals,
                        snapshot = new
                        {
                            evaluationResult.Snapshot.Allow,
                            evaluationResult.Snapshot.Deny,
                            evaluationResult.Snapshot.Escalate
                        },
                        matches = evaluationResult.Matches.Select(match => new
                        {
                            match.Rule,
                            match.RuleType,
                            Source = match.Source.ToString(),
                            match.SourceId,
                            Layer = match.Layer?.ToString()
                        }),
                        trace = evaluationResult.Trace.Select(entry => new
                        {
                            Source = entry.Source.ToString(),
                            entry.SourceId,
                            Layer = entry.Layer?.ToString(),
                            entry.AddedAllow,
                            entry.AddedDeny,
                            entry.AddedEscalate
                        })
                    }
                }, metadataOptions);
            }

            // Create task
            // Capture request context from authenticated principal claims (post dev header removal).
            // Claims provided by JWT issuance: tenant_slug (when tenant selected) and email.
            var requestTenant = ctx.User.FindFirst("tenant_slug")?.Value;
            var requestUser = ctx.User.FindFirst("email")?.Value;

            var task = new AgentTask(Guid.NewGuid(), agent.Id, req.Input.RootElement.GetRawText())
            {
                Status = AgentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                RequestTenant = string.IsNullOrWhiteSpace(requestTenant) ? null : requestTenant.Trim(),
                RequestUser = string.IsNullOrWhiteSpace(requestUser) ? null : requestUser.Trim(),
                GuardrailDecision = guardrailDecision,
                GuardrailMetadataJson = guardrailMetadataJson
            };

            if (guardrailDecision is GuardrailDecision.Deny)
            {
                task.Status = AgentStatus.Failed;
                task.ErrorMessage = $"Guardrail denied: {evaluationResult?.ReasonCode ?? "blocked"}";
                task.FinishedAt = DateTime.UtcNow;
            }
            else if (guardrailDecision is GuardrailDecision.Escalate)
            {
                task.Status = AgentStatus.Failed;
                task.ErrorMessage = $"Guardrail escalation required: {evaluationResult?.ReasonCode ?? "blocked"}";
                task.FinishedAt = DateTime.UtcNow;
            }

            db.Add(task);
            await db.SaveChangesAsync(ct);

            var shouldEnqueue = guardrailDecision is null or GuardrailDecision.Allow;

            if (!shouldEnqueue)
            {
                var blockedSummary = new AgentTaskSummary(
                    task.Id,
                    task.AgentId,
                    task.Status.ToString(),
                    task.CreatedAt,
                    task.StartedAt,
                    task.FinishedAt,
                    task.TotalTokens,
                    task.GuardrailDecision
                );

                return Results.Created($"/api/agent-tasks/{task.Id}", blockedSummary);
            }

            // Optional test hooks (Development only):
            // 1) allow a tiny delay before enqueue via header
            // 2) allow complete suppression of enqueue to keep task Pending for deterministic testing
            try
            {
                var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
                if (env.IsDevelopment())
                {
                    // Suppress enqueue entirely if requested
                    var suppress = ctx.Request.Headers["x-test-suppress-enqueue"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(suppress) && (suppress == "1" || suppress.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Return 201 without enqueue; task remains Pending
                        Appostolic.Api.Application.Agents.Runtime.Metrics.RecordTaskCreated(task.RequestTenant, task.AgentId);
                        var summarySuppressed = new AgentTaskSummary(
                            task.Id,
                            task.AgentId,
                            task.Status.ToString(),
                            task.CreatedAt,
                            task.StartedAt,
                            task.FinishedAt,
                            task.TotalTokens,
                            task.GuardrailDecision
                        );
                        return Results.Created($"/api/agent-tasks/{task.Id}", summarySuppressed);
                    }

                    var delayHeader = ctx.Request.Headers["x-test-enqueue-delay-ms"].FirstOrDefault();
                    if (int.TryParse(delayHeader, out var delayMs) && delayMs > 0)
                    {
                        await Task.Delay(delayMs, ct);
                    }
                }
            }
            catch { /* ignore */ }

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
                task.TotalTokens,
                task.GuardrailDecision
            );

            return Results.Created($"/api/agent-tasks/{task.Id}", summary);
        })
    .WithSummary("Create an agent task and enqueue it for processing")
    .WithDescription("Validates the agent, persists the task (status=Pending), and enqueues it via IAgentTaskQueue. Authenticated JWT required (dev headers removed).");

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

            JsonDocument? guardrailMetadataDoc = null;
            if (!string.IsNullOrWhiteSpace(task.GuardrailMetadataJson))
            {
                try { guardrailMetadataDoc = JsonDocument.Parse(task.GuardrailMetadataJson!); } catch { /* ignore */ }
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
                task.ErrorMessage,
                task.GuardrailDecision,
                guardrailMetadataDoc
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

        // POST /api/agent-tasks/{id}/retry
        group.MapPost("{id:guid}/retry", async (
            Guid id,
            AppDbContext db,
            IAgentTaskQueue queue,
            CancellationToken ct) =>
        {
            var src = await db.Set<AgentTask>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (src is null)
            {
                return Results.NotFound();
            }

            // Reject when source is not terminal
            if (src.Status is AgentStatus.Pending or AgentStatus.Running)
            {
                return Results.Conflict(new { message = "Source task is not terminal" });
            }

            var newTask = new AgentTask(Guid.NewGuid(), src.AgentId, src.InputJson)
            {
                Status = AgentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                RequestTenant = src.RequestTenant,
                RequestUser = src.RequestUser
            };

            db.Add(newTask);
            await db.SaveChangesAsync(ct);

            await queue.EnqueueAsync(newTask.Id, ct);

            var summary = new AgentTaskSummary(
                newTask.Id,
                newTask.AgentId,
                newTask.Status.ToString(),
                newTask.CreatedAt,
                newTask.StartedAt,
                newTask.FinishedAt,
                newTask.TotalTokens,
                newTask.GuardrailDecision
            );

            return Results.Created($"/api/agent-tasks/{newTask.Id}", summary);
        })
        .WithSummary("Retry an agent task")
        .WithDescription("Clones a terminal task (Failed/Canceled/Succeeded) into a new Pending task with same agent and input, enqueues it, and returns 201 with Location.");

        // POST /api/agent-tasks/{id}/cancel
        group.MapPost("{id:guid}/cancel", async (
            Guid id,
            AppDbContext db,
            Application.Agents.Queue.AgentTaskCancelRegistry cancelRegistry,
            CancellationToken ct) =>
        {
            var task = await db.Set<AgentTask>().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null)
            {
                return Results.NotFound();
            }

            // If already terminal, reject
            if (task.Status is AgentStatus.Succeeded or AgentStatus.Failed or AgentStatus.Canceled)
            {
                return Results.Conflict(new { message = "Already terminal" });
            }

            if (task.Status == AgentStatus.Pending)
            {
                task.Status = AgentStatus.Canceled;
                task.ErrorMessage = "Canceled";
                task.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Accepted($"/api/agent-tasks/{task.Id}", new { id = task.Id, status = task.Status.ToString() });
            }

            if (task.Status == AgentStatus.Running)
            {
                cancelRegistry.RequestCancel(task.Id);
                // Orchestrator will observe and mark terminal shortly
                return Results.Accepted($"/api/agent-tasks/{task.Id}", new { id = task.Id, status = task.Status.ToString() });
            }

            // Fallback (shouldn't be reached)
            return Results.Accepted($"/api/agent-tasks/{task.Id}", new { id = task.Id, status = task.Status.ToString() });
        })
        .WithSummary("Request cancellation of an agent task")
        .WithDescription("If Pending, cancels immediately. If Running, records a cooperative cancel request; the worker will transition to Canceled shortly. Terminal tasks return 409.");

        // GET /api/agent-tasks (list, optional filters)
        group.MapGet("", async (
                HttpContext http,
                string? status,
                Guid? agentId,
                DateTime? from,
                DateTime? to,
                string? q,
                int? take,
                int? skip,
                AppDbContext db,
                CancellationToken ct) =>
            {
                var query = db.Set<AgentTask>().AsNoTracking().AsQueryable();

                // status filter (case-insensitive enum)
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (!Enum.TryParse<AgentStatus>(status, ignoreCase: true, out var st))
                        return Results.BadRequest(new { error = "invalid status" });
                    query = query.Where(t => t.Status == st);
                }

                // agentId filter
                if (agentId.HasValue && agentId.Value != Guid.Empty)
                {
                    query = query.Where(t => t.AgentId == agentId.Value);
                }

                // date range on CreatedAt (UTC)
                if (from.HasValue)
                {
                    var f = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
                    query = query.Where(t => t.CreatedAt >= f);
                }
                if (to.HasValue)
                {
                    var tmax = DateTime.SpecifyKind(to.Value, DateTimeKind.Utc);
                    query = query.Where(t => t.CreatedAt <= tmax);
                }

                // free-text q: matches Id, RequestUser, or InputJson text
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var qval = q.Trim();

                    // Try Guid parse for Id match
                    if (Guid.TryParse(qval, out var qId))
                    {
                        query = query.Where(t => t.Id == qId || t.RequestUser!.Contains(qval));
                    }
                    else
                    {
                        // Provider-agnostic implementation: use Npgsql ILIKE when available; otherwise fall back to case-insensitive Contains for in-memory tests
                        var provider = db.Database.ProviderName;
                        if (!string.IsNullOrEmpty(provider) && provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                        {
                            var like = $"%{qval}%";
                            query = query.Where(t =>
                                (t.RequestUser != null && EF.Functions.ILike(t.RequestUser, like)) ||
                                EF.Functions.ILike(t.InputJson, like)
                            );
                        }
                        else
                        {
                            var low = qval.ToLowerInvariant();
                            query = query.Where(t =>
                                (t.RequestUser != null && t.RequestUser.ToLower().Contains(low)) ||
                                t.InputJson.ToLower().Contains(low)
                            );
                        }
                    }
                }

                // Total count for X-Total-Count header
                var total = await query.CountAsync(ct);

                // paging
                int takeVal = take.GetValueOrDefault(20);
                int skipVal = skip.GetValueOrDefault(0);
                if (takeVal <= 0) takeVal = 20;
                if (skipVal < 0) skipVal = 0;

                var items = await query
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
                        t.TotalTokens,
                        t.GuardrailDecision
                    ))
                    .ToListAsync(ct);

                http.Response.Headers["X-Total-Count"] = total.ToString();
                return Results.Ok(items);
            })
            .WithSummary("List agent tasks (paged)")
            .WithDescription("Returns AgentTaskSummary[] ordered by CreatedAt DESC. Optional filters: status, agentId, from, to, q. Supports take/skip paging and sets X-Total-Count header.");

    return app;
    }
}
