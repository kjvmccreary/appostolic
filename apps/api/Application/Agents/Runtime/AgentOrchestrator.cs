using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Tools;

namespace Appostolic.Api.Application.Agents.Runtime;

public interface IAgentOrchestrator
{
    Task RunAsync(Agent agent, AgentTask task, string tenant, string user, CancellationToken ct);
}

public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private readonly AppDbContext _db;
    private readonly IModelAdapter _model;
    private readonly ToolRegistry _tools;
    private readonly ITraceWriter _trace;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(AppDbContext db, IModelAdapter model, ToolRegistry tools, ITraceWriter trace, ILogger<AgentOrchestrator> logger)
    {
        _db = db;
        _model = model;
        _tools = tools;
        _trace = trace;
        _logger = logger;
    }

    public async Task RunAsync(Agent agent, AgentTask task, string tenant, string user, CancellationToken ct)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["taskId"] = task.Id,
            ["agentId"] = agent.Id
        });

        var state = new AgentState
        {
            TaskId = task.Id,
            AgentId = agent.Id,
            InputJson = JsonDocument.Parse(task.InputJson),
            Scratchpad = JsonDocument.Parse("{}"),
            StepNumber = 1,
            MaxSteps = agent.MaxSteps,
            IsComplete = false
        };

        if (task.StartedAt == null)
        {
            task.StartedAt = DateTime.UtcNow;
        }
        task.Status = AgentStatus.Running;
        await _db.SaveChangesAsync(ct);

        JsonDocument? lastToolOutput = null;

    while (state.StepNumber <= state.MaxSteps)
        {
            ct.ThrowIfCancellationRequested();
            using var stepScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["taskId"] = task.Id,
                ["agentId"] = agent.Id,
                ["step"] = state.StepNumber,
                ["tenant"] = tenant,
                ["user"] = user,
                ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
            });

            // Build model prompt context
            var ctxObj = new Dictionary<string, object?>
            {
                ["input"] = state.InputJson.RootElement,
                ["scratchpad"] = state.Scratchpad.RootElement
            };
            if (lastToolOutput is not null)
            {
                ctxObj["lastTool"] = new { output = lastToolOutput.RootElement };
            }
            var contextJson = JsonSerializer.SerializeToUtf8Bytes(ctxObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            using var contextDoc = JsonDocument.Parse(contextJson);
            var prompt = new ModelPrompt(agent.SystemPrompt, contextDoc);

            // Model decision with span
            int modelDuration;
            Model.ModelDecision decision;
            using (var activity = Telemetry.AgentSource.StartActivity("agent.model", System.Diagnostics.ActivityKind.Internal))
            {
                activity?.SetTag("agent.id", agent.Id);
                activity?.SetTag("task.id", task.Id);
                activity?.SetTag("tenant", tenant);
                activity?.SetTag("user", user);
                activity?.SetTag("step", state.StepNumber);
                activity?.SetTag("model.name", agent.Model);
                activity?.SetTag("maxSteps", state.MaxSteps);

                var modelStart = DateTime.UtcNow;
                decision = await _model.DecideAsync(prompt, ct);
                modelDuration = (int)(DateTime.UtcNow - modelStart).TotalMilliseconds;
                activity?.SetTag("duration.ms", modelDuration);
            }

            // Branch on decision
            if (decision.Action is FinalAnswer fa)
            {
                task.ResultJson = fa.Result.RootElement.GetRawText();
                task.Status = AgentStatus.Succeeded;
                task.FinishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Task succeeded at step {Step}", state.StepNumber);
                return;
            }
            else if (decision.Action is UseTool ut)
            {
                // Allocate two step numbers per iteration to avoid collisions with subsequent iterations:
                //  - model trace at currentStep
                //  - tool trace at currentStep + 1
                //  Then advance to currentStep + 2 for the next loop.
                var currentStep = state.StepNumber;

                // Trace the model step only when planning a tool
                await _trace.WriteModelStepAsync(task, currentStep, prompt, decision, modelDuration, ct);
                // Reject empty tool name
                if (string.IsNullOrWhiteSpace(ut.Name))
                {
                    using (var toolActivity = Telemetry.ToolSource.StartActivity($"tool.(missing)", System.Diagnostics.ActivityKind.Internal))
                    {
                        toolActivity?.SetTag("tool.name", "(missing)");
                        toolActivity?.SetTag("tenant", tenant);
                        toolActivity?.SetTag("user", user);
                        toolActivity?.SetTag("success", false);
                        toolActivity?.SetTag("duration.ms", 0);
                        toolActivity?.SetTag("error", "Tool name is required");
                    }
                    await _trace.WriteToolStepAsync(task, state.StepNumber, "(missing)", ut.Input, null, success: false, durationMs: 0, promptTokens: decision.PromptTokens, completionTokens: decision.CompletionTokens, error: "Tool name is required", ct: ct);
                    task.Status = AgentStatus.Failed;
                    task.ErrorMessage = "Tool name is required";
                    task.FinishedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogWarning("Empty tool name at step {Step}", state.StepNumber);
                    return;
                }
                // Allowlist check
                var allowed = agent.ToolAllowlist?.Any(n => string.Equals(n, ut.Name, StringComparison.OrdinalIgnoreCase)) == true;
                if (!allowed)
                {
                    using (var toolActivity = Telemetry.ToolSource.StartActivity($"tool.{ut.Name}", System.Diagnostics.ActivityKind.Internal))
                    {
                        toolActivity?.SetTag("tool.name", ut.Name);
                        toolActivity?.SetTag("tenant", tenant);
                        toolActivity?.SetTag("user", user);
                        toolActivity?.SetTag("success", false);
                        toolActivity?.SetTag("duration.ms", 0);
                        toolActivity?.SetTag("error", "Tool not allowed");
                    }
                    var denyDuration = 0;
                    await _trace.WriteToolStepAsync(task, state.StepNumber, ut.Name, ut.Input, null, success: false, durationMs: denyDuration, promptTokens: decision.PromptTokens, completionTokens: decision.CompletionTokens, error: "Tool not allowed", ct: ct);
                    task.Status = AgentStatus.Failed;
                    task.ErrorMessage = $"Tool not allowed: {ut.Name}";
                    task.FinishedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogWarning("Denied tool {Tool} at step {Step}", ut.Name, state.StepNumber);
                    return;
                }

                // Resolve tool
                if (!_tools.TryGet(ut.Name, out var tool) || tool is null)
                {
                    using (var toolActivity = Telemetry.ToolSource.StartActivity($"tool.{ut.Name}", System.Diagnostics.ActivityKind.Internal))
                    {
                        toolActivity?.SetTag("tool.name", ut.Name);
                        toolActivity?.SetTag("tenant", tenant);
                        toolActivity?.SetTag("user", user);
                        toolActivity?.SetTag("success", false);
                        toolActivity?.SetTag("duration.ms", 0);
                        toolActivity?.SetTag("error", "Tool not found");
                    }
                    await _trace.WriteToolStepAsync(task, state.StepNumber, ut.Name, ut.Input, null, success: false, durationMs: 0, promptTokens: decision.PromptTokens, completionTokens: decision.CompletionTokens, error: "Tool not found", ct: ct);
                    task.Status = AgentStatus.Failed;
                    task.ErrorMessage = $"Tool not found: {ut.Name}";
                    task.FinishedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    _logger.LogWarning("Tool not found {Tool} at step {Step}", ut.Name, state.StepNumber);
                    return;
                }

                // Execute tool
                var execCtx = new Tools.ToolExecutionContext(task.Id, currentStep, tenant, user, _logger);
                int toolDuration;
                Tools.ToolCallResult result;
                using (var toolActivity = Telemetry.ToolSource.StartActivity($"tool.{tool.Name}", System.Diagnostics.ActivityKind.Internal))
                {
                    toolActivity?.SetTag("tool.name", tool.Name);
                    toolActivity?.SetTag("tenant", tenant);
                    toolActivity?.SetTag("user", user);
                    var toolStart = DateTime.UtcNow;
                    result = await tool.InvokeAsync(new Tools.ToolCallRequest(tool.Name, ut.Input), execCtx, ct);
                    toolDuration = (int)(DateTime.UtcNow - toolStart).TotalMilliseconds;
                    toolActivity?.SetTag("success", result.Success);
                    if (!result.Success && result.Error is not null)
                    {
                        toolActivity?.SetTag("error", result.Error);
                    }
                    toolActivity?.SetTag("duration.ms", toolDuration);
                }
                await _trace.WriteToolStepAsync(task, currentStep + 1, tool.Name, ut.Input, result.Output, result.Success, toolDuration, decision.PromptTokens, decision.CompletionTokens, result.Error, ct);

                lastToolOutput = result.Output;

                // Update scratchpad.lastTool
                var sp = new Dictionary<string, object?>
                {
                    ["lastTool"] = new { name = tool.Name, output = result.Output?.RootElement }
                };
                var spJson = JsonSerializer.SerializeToUtf8Bytes(sp);
                state.Scratchpad = JsonDocument.Parse(spJson);
                // Advance by 2 to reserve unique step numbers per iteration
                state.StepNumber = currentStep + 2;
                continue;
            }
            else
            {
                task.Status = AgentStatus.Failed;
                task.ErrorMessage = "Unknown model action";
                task.FinishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogError("Unknown model action at step {Step}", state.StepNumber);
                return;
            }
        }

        // Step cap reached
        task.Status = AgentStatus.Failed;
        task.ErrorMessage = "MaxSteps exceeded";
        task.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("MaxSteps exceeded after step {Step}", state.StepNumber);
    }
}
