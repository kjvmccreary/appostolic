using System.Text.Json;
using Appostolic.Api.Application.Agents.Tools;
using Microsoft.AspNetCore.Authorization;

namespace Appostolic.Api.App.Endpoints;

public static class DevToolsEndpoints
{
    public static void MapDevToolsEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost("/api/dev/tool-call", async (
            HttpContext http,
            ToolRegistry registry,
            ILoggerFactory loggerFactory,
            DevToolCallRequest body,
            CancellationToken ct) =>
        {
            // Require headers for dev identity and tenant
            var user = http.Request.Headers["x-dev-user"].FirstOrDefault();
            var tenant = http.Request.Headers["x-tenant"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(tenant))
            {
                return Results.BadRequest(new { error = "Missing x-dev-user or x-tenant headers" });
            }

            if (!registry.TryGet(body.Name, out var tool) || tool is null)
            {
                return Results.BadRequest(new { error = $"Unknown tool '{body.Name}'" });
            }

            var input = body.Input ?? JsonDocument.Parse("{}");
            var req = new ToolCallRequest(tool.Name, input);
            var ctx = new ToolExecutionContext(
                TaskId: Guid.NewGuid(),
                StepNumber: 1,
                Tenant: tenant!,
                User: user!,
                Logger: loggerFactory.CreateLogger("DevToolInvoker"));

            var result = await tool.InvokeAsync(req, ctx, ct);
            return Results.Ok(result);
        })
        .WithSummary("Dev-only tool invoker (S1-09)")
        .RequireAuthorization();
    }

    public sealed record DevToolCallRequest(string Name, JsonDocument? Input);
}
