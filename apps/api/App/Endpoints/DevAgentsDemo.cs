using System.Text.Json;
using Appostolic.Api.Application.Agents;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Domain.Agents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class DevAgentsDemo
{
    public static void MapDevAgentsDemoEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost("/api/dev/agents/demo", async (
            HttpContext http,
            AppDbContext db,
            IAgentOrchestrator orchestrator,
            DemoRequest body,
            CancellationToken ct) =>
        {
            // Require dev headers
            var user = http.Request.Headers["x-dev-user"].FirstOrDefault();
            var tenant = http.Request.Headers["x-tenant"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(tenant))
            {
                return Results.BadRequest(new { error = "Missing x-dev-user or x-tenant headers" });
            }

            var agent = AgentRegistry.FindById(AgentRegistry.ResearchAgentId);
            if (agent is null)
            {
                return Results.Problem("ResearchAgent not found in registry.", statusCode: 500);
            }

            // Create a new task
            var input = JsonSerializer.Serialize(new { topic = body.Topic });
            var task = new AgentTask(Guid.NewGuid(), agent.Id, input);
            db.Add(task);
            await db.SaveChangesAsync(ct);

            // Run inline (no queue)
            await orchestrator.RunAsync(agent, task, tenant!, user!, ct);

            var traces = await db.Set<AgentTrace>().AsNoTracking()
                .Where(t => t.TaskId == task.Id)
                .OrderBy(t => t.StepNumber)
                .ToListAsync(ct);

            return Results.Ok(new { task, traces });
        })
        .WithSummary("Dev-only: run ResearchAgent inline (S1-09 demo)")
        .RequireAuthorization();
    }

    public sealed record DemoRequest(string Topic);
}
