using Appostolic.Api.Application.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Appostolic.Api.App.Endpoints;

public static class DevAgentsEndpoints
{
    public static IEndpointRouteBuilder MapDevAgentsEndpoints(this IEndpointRouteBuilder app, IHostEnvironment env)
    {
        if (!env.IsDevelopment())
        {
            // Do not map outside Development
            return app;
        }

        var group = app
            .MapGroup("/api/dev/agents")
            .RequireAuthorization()
            .WithTags("DevAgents");

        // GET /api/dev/agents
        group.MapGet("", () =>
        {
            var items = AgentRegistry.All.Select(a => new
            {
                id = a.Id,
                name = a.Name,
                model = a.Model,
                temperature = a.Temperature,
                maxSteps = a.MaxSteps,
                toolAllowlist = a.ToolAllowlist
            });
            return Results.Ok(items);
        })
        .WithSummary("List dev agent seeds")
        .WithDescription("Development-only. Lists AgentRegistry fixtures for UI dropdowns.");

        return app;
    }
}
