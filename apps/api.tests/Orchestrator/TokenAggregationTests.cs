using System.Text.Json;
using Appostolic.Api.App.Options;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.Domain.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Appostolic.Api.Tests.Orchestrator;

public class TokenAggregationTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private sealed class TwoStepScriptedModel : IModelAdapter
    {
        private int _call;
        public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
        {
            _call++;
            if (_call == 1)
            {
                return Task.FromResult(new ModelDecision(
                    new UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":1}")),
                    8,
                    2,
                    "plan"));
            }
            return Task.FromResult(new ModelDecision(
                new FinalAnswer(JsonDocument.Parse("{\"ok\":true}")),
                6,
                3,
                "final"));
        }
    }

    [Fact]
    public async Task AggregatesTokens_And_ComputesCost_WhenPricingEnabled()
    {
        using var db = CreateDb();
        var agent = new Agent(Guid.NewGuid(), "tester", "system", new[] { "web.search" }, model: "gpt-4o-mini", temperature: 0.0, maxSteps: 8);
        db.Add(agent);
        var task = new AgentTask(Guid.NewGuid(), agent.Id, JsonSerializer.Serialize(new { q = "intro" }));
        db.Add(task);
        await db.SaveChangesAsync();

        var reg = new ToolRegistry(new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        var trace = new TraceWriter(db, NullLogger<TraceWriter>.Instance);
        var pricing = Options.Create(new ModelPricingOptions
        {
            Enabled = true,
            Models = new()
            {
                ["gpt-4o-mini"] = new ModelPrice { InputPer1K = 0.15m, OutputPer1K = 0.6m }
            }
        });
    var cancel = new Appostolic.Api.Application.Agents.Queue.AgentTaskCancelRegistry();
    var orchestrator = new AgentOrchestrator(db, new TwoStepScriptedModel(), reg, trace, NullLogger<AgentOrchestrator>.Instance, pricing, cancel);

        await orchestrator.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Succeeded);
        task.TotalPromptTokens.Should().Be(8 + 6);
        task.TotalCompletionTokens.Should().Be(2 + 3);
        task.TotalTokens.Should().Be(19);

        task.EstimatedCostUsd.Should().NotBeNull();
        // exact: (14/1000)*0.15 + (5/1000)*0.6 = 0.0021 + 0.003 = 0.0051
        task.EstimatedCostUsd!.Should().BeApproximately(0.0051m, 0.0001m);
    }

    [Fact]
    public async Task CostIsNull_WhenPricingDisabled()
    {
        using var db = CreateDb();
        var agent = new Agent(Guid.NewGuid(), "tester", "system", new[] { "web.search" }, model: "gpt-4o-mini", temperature: 0.0, maxSteps: 8);
        db.Add(agent);
        var task = new AgentTask(Guid.NewGuid(), agent.Id, JsonSerializer.Serialize(new { q = "intro" }));
        db.Add(task);
        await db.SaveChangesAsync();

        var reg = new ToolRegistry(new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        var trace = new TraceWriter(db, NullLogger<TraceWriter>.Instance);
        var pricing = Options.Create(new ModelPricingOptions { Enabled = false });
    var cancel = new Appostolic.Api.Application.Agents.Queue.AgentTaskCancelRegistry();
    var orchestrator = new AgentOrchestrator(db, new TwoStepScriptedModel(), reg, trace, NullLogger<AgentOrchestrator>.Instance, pricing, cancel);

        await orchestrator.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Succeeded);
        task.TotalPromptTokens.Should().BeGreaterThan(0);
        task.TotalCompletionTokens.Should().BeGreaterThan(0);
        task.EstimatedCostUsd.Should().BeNull();
    }
}
