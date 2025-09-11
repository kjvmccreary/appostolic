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
        var api = app.MapGroup("/api").RequireAuthorization();

        // POST /api/agent-tasks
        api.MapPost("/agent-tasks", async (
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
            var task = new AgentTask(Guid.NewGuid(), agent.Id, req.Input.RootElement.GetRawText())
            {
                Status = AgentStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            db.Add(task);
            await db.SaveChangesAsync(ct);

            // Enqueue for processing
            await queue.EnqueueAsync(task.Id, ct);

            var summary = new AgentTaskSummary(
                task.Id,
                task.AgentId,
                task.Status.ToString(),
                task.CreatedAt,
                task.StartedAt,
                task.FinishedAt
            );

            return Results.Created($"/api/agent-tasks/{task.Id}", summary);
        })
        .WithSummary("Create an agent task and enqueue it for processing")
        .WithDescription("Validates the agent, persists the task (status=Pending), and enqueues it via IAgentTaskQueue. Requires dev headers in Development.");

        return app;
    }
}
