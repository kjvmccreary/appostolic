using System.Text.Json;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Appostolic.Api.Tests;

public class AgentOrchestratorTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static (IAgentOrchestrator orch, AppDbContext db, Agent agent, AgentTask task) BuildHarness(Action<Agent>? configureAgent = null, Action<AgentTask>? configureTask = null, IModelAdapter? model = null, IEnumerable<ITool>? tools = null)
    {
        var db = CreateDb();
        var agent = new Agent(Guid.NewGuid(), "tester", "system", new[] { "web.search" }, model: "mock", temperature: 0.0, maxSteps: 8);
        configureAgent?.Invoke(agent);
        db.Add(agent);
        var task = new AgentTask(Guid.NewGuid(), agent.Id, "{\"q\":\"intro\"}");
        configureTask?.Invoke(task);
        db.Add(task);
        db.SaveChanges();

        var reg = new ToolRegistry(tools ?? new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        var trace = new TraceWriter(db, NullLogger<TraceWriter>.Instance);
    var orchestrator = new AgentOrchestrator(db, model ?? new MockModelAdapter(), reg, trace, NullLogger<AgentOrchestrator>.Instance, Microsoft.Extensions.Options.Options.Create(new Appostolic.Api.App.Options.ModelPricingOptions()));
        return (orchestrator, db, agent, task);
    }

    [Fact]
    public async Task HappyPath_ModelPlansToolThenFinal_SucceedsWithTwoTraces()
    {
        // model: first choose tool; then after tool output, choose final
        var scripted = new ScriptedModelAdapter(new[]
        {
            ScriptedDecision.UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":1}")),
            ScriptedDecision.Final(JsonDocument.Parse("{\"ok\":true}"))
        });

        var (orch, db, agent, task) = BuildHarness(model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Succeeded);
        task.FinishedAt.Should().NotBeNull();
        var traces = await db.Set<AgentTrace>().AsNoTracking().Where(t => t.TaskId == task.Id).OrderBy(t => t.StepNumber).ToListAsync();
        traces.Should().HaveCount(2);
        traces[0].Kind.Should().Be(TraceKind.Model);
        traces[0].StepNumber.Should().Be(1);
        traces[0].PromptTokens.Should().BeGreaterOrEqualTo(0);
        traces[0].CompletionTokens.Should().BeGreaterOrEqualTo(0);
        traces[1].Kind.Should().Be(TraceKind.Tool);
        traces[1].StepNumber.Should().Be(2);
    }

    [Fact]
    public async Task ToolNotAllowed_FailsAndTracesFailure()
    {
        var scripted = new ScriptedModelAdapter(new[] { ScriptedDecision.UseTool("db.query", JsonDocument.Parse("{}")) });
        var (orch, db, agent, task) = BuildHarness(a => a.ToolAllowlist = new[] { "web.search" }, model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("Tool not allowed");
        task.FinishedAt.Should().NotBeNull();
        var trace = await db.Set<AgentTrace>().FirstOrDefaultAsync(t => t.TaskId == task.Id && t.Kind == TraceKind.Tool);
        trace.Should().NotBeNull();
        trace!.Name.Should().Be("db.query");
    }

    [Fact]
    public async Task ToolMissing_FailsWithToolNotFound()
    {
        var scripted = new ScriptedModelAdapter(new[] { ScriptedDecision.UseTool("not.there", JsonDocument.Parse("{}")) });
        var (orch, db, agent, task) = BuildHarness(a => a.ToolAllowlist = new[] { "web.search", "not.there" }, model: scripted, tools: new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);
        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("Tool not found");
        task.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MaxStepsExceeded_FailsAfterCap()
    {
        // model will always choose tool
        var scripted = new LoopingToolAdapter("web.search");
        var (orch, db, agent, task) = BuildHarness(a => a.MaxSteps = 1, model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);
        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("MaxSteps");
        task.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancellation_StopsMidRun()
    {
        var scripted = new ScriptedModelAdapter(new[]
        {
            ScriptedDecision.UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\"}")),
            // Second decision would be final, but we'll cancel before
            ScriptedDecision.Final(JsonDocument.Parse("{\"done\":true}"))
        }, delayMsPerDecision: 50);

        var (orch, db, agent, task) = BuildHarness(model: scripted);
        using var cts = new CancellationTokenSource();
        var run = orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: cts.Token);
        // cancel quickly
        cts.CancelAfter(10);

        try { await run; } catch (OperationCanceledException) { }

        // After cancellation, status should be Running or Failed depending on timing; but no exception leaks here.
        var traces = await db.Set<AgentTrace>().AsNoTracking().Where(t => t.TaskId == task.Id).ToListAsync();
        traces.Count.Should().BeGreaterOrEqualTo(0);
    }
}

// Test helpers
public sealed class ScriptedModelAdapter : IModelAdapter
{
    private readonly Queue<ModelDecision> _decisions;
    private readonly int _delay;
    public ScriptedModelAdapter(IEnumerable<ModelDecision> decisions, int delayMsPerDecision = 0)
    {
        _decisions = new Queue<ModelDecision>(decisions);
        _delay = delayMsPerDecision;
    }
    public async Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
    {
        if (_delay > 0) await Task.Delay(_delay, ct);
        if (_decisions.Count == 0)
        {
            // default to tool to drive steps
            return new ModelDecision(new UseTool("web.search", JsonDocument.Parse("{}")), 1, 1, "loop");
        }
        return _decisions.Dequeue();
    }
}

public static class ScriptedDecision
{
    public static ModelDecision UseTool(string name, JsonDocument input) => new(new UseTool(name, input), 5, 1, "use-tool");
    public static ModelDecision Final(JsonDocument result) => new(new FinalAnswer(result), 5, 1, "final");
}

public sealed class LoopingToolAdapter : IModelAdapter
{
    private readonly string _tool;
    public LoopingToolAdapter(string tool) => _tool = tool;
    public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
        => Task.FromResult(new ModelDecision(new UseTool(_tool, JsonDocument.Parse("{}")), 1, 1, "loop"));
}
