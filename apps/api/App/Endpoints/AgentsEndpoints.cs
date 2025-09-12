using Appostolic.Api.Application.Agents.Api;
using Appostolic.Api.Domain.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Appostolic.Api.App.Endpoints;

public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/agents")
            .RequireAuthorization()
            .WithTags("Agents");

        // GET /api/agents
        group.MapGet("", async (int? take, int? skip, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Set<Agent>().AsNoTracking();
            int takeVal = take.GetValueOrDefault(50);
            int skipVal = skip.GetValueOrDefault(0);
            if (takeVal <= 0) takeVal = 50;
            if (skipVal < 0) skipVal = 0;

            var items = await q
                .OrderByDescending(a => a.CreatedAt)
                .Skip(skipVal)
                .Take(takeVal)
                .Select(a => new AgentListItem(
                    a.Id, a.Name, a.Model, a.Temperature, a.MaxSteps, a.CreatedAt, a.UpdatedAt
                ))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithSummary("List agents (paged)");

        // GET /api/agents/{id}
        group.MapGet("{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var a = await db.Set<Agent>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            return Results.Ok(new AgentDetails(
                a.Id, a.Name, a.Model, a.Temperature, a.MaxSteps, a.SystemPrompt, a.ToolAllowlist, a.CreatedAt, a.UpdatedAt
            ));
        })
        .WithSummary("Get agent by id");

        // POST /api/agents
        group.MapPost("", async (AgentUpsertRequest req, AppDbContext db, CancellationToken ct) =>
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest(new { error = "Name is required" });
            if (req.Temperature < 0 || req.Temperature > 2) return Results.BadRequest(new { error = "Temperature must be between 0 and 2" });
            if (req.MaxSteps < 1 || req.MaxSteps > 50) return Results.BadRequest(new { error = "MaxSteps must be between 1 and 50" });

            var exists = await db.Set<Agent>().AnyAsync(a => a.Name == req.Name, ct);
            if (exists) return Results.Conflict(new { error = "Agent with this name already exists" });

            var agent = new Agent(Guid.NewGuid(), req.Name, req.SystemPrompt ?? string.Empty, req.ToolAllowlist ?? Array.Empty<string>(), req.Model, req.Temperature, req.MaxSteps);
            db.Add(agent);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/agents/{agent.Id}", new AgentDetails(agent.Id, agent.Name, agent.Model, agent.Temperature, agent.MaxSteps, agent.SystemPrompt, agent.ToolAllowlist, agent.CreatedAt, agent.UpdatedAt));
        })
        .WithSummary("Create a new agent");

        // PUT /api/agents/{id}
        group.MapPut("{id:guid}", async (Guid id, AgentUpsertRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var agent = await db.Set<Agent>().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) return Results.NotFound();

            // Enforce unique name (excluding self)
            var nameTaken = await db.Set<Agent>().AnyAsync(a => a.Name == req.Name && a.Id != id, ct);
            if (nameTaken) return Results.Conflict(new { error = "Agent with this name already exists" });

            // Field-level validation uses domain setters (will throw on invalid)
            agent.Name = req.Name;
            agent.Model = req.Model;
            agent.Temperature = req.Temperature;
            agent.MaxSteps = req.MaxSteps;
            agent.SystemPrompt = req.SystemPrompt ?? string.Empty;
            agent.ToolAllowlist = req.ToolAllowlist ?? Array.Empty<string>();
            agent.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new AgentDetails(agent.Id, agent.Name, agent.Model, agent.Temperature, agent.MaxSteps, agent.SystemPrompt, agent.ToolAllowlist, agent.CreatedAt, agent.UpdatedAt));
        })
        .WithSummary("Update an existing agent");

        // DELETE /api/agents/{id}
        group.MapDelete("{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var agent = await db.Set<Agent>().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (agent is null) return Results.NotFound();
            db.Remove(agent);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Delete an agent");

        return app;
    }
}
