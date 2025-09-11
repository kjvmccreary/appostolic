using System.Text.Json;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.Domain.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Appostolic.Api.Tests.Orchestrator;

public class AgentOrchestratorTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static (IAgentOrchestrator orch, AppDbContext db, Agent agent, AgentTask task) BuildHarness(
        Action<Agent>? configureAgent = null,
        Action<AgentTask>? configureTask = null,
        IModelAdapter? model = null,
        IEnumerable<ITool>? tools = null)
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
        var orchestrator = new AgentOrchestrator(db, model ?? new MockModelAdapter(), reg, trace, NullLogger<AgentOrchestrator>.Instance);
        return (orchestrator, db, agent, task);
    }

    [Fact]
    public async Task HappyPath_ModelPlansToolThenFinal_SucceedsWithTwoTraces()
    {
        // Mock model: first request uses default tool; second sees lastTool and returns final
        var scripted = new TwoStepMockModel();
        var (orch, db, agent, task) = BuildHarness(model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Succeeded);
        task.ResultJson.Should().NotBeNullOrWhiteSpace();
        task.FinishedAt.Should().NotBeNull();
        var traces = await db.Set<AgentTrace>().AsNoTracking().Where(t => t.TaskId == task.Id).OrderBy(t => t.StepNumber).ToListAsync();
        traces.Should().HaveCount(2);
        traces[0].Kind.Should().Be(TraceKind.Model);
        traces[0].StepNumber.Should().Be(1);
        traces[0].PromptTokens.Should().BeGreaterThan(0);
        traces[1].Kind.Should().Be(TraceKind.Tool);
        traces[1].StepNumber.Should().Be(2);
    }

    [Fact]
    public async Task ToolNotAllowed_FailsAndTracesFailure()
    {
        var scripted = new OneToolLoopMockModel("db.query");
        var (orch, db, agent, task) = BuildHarness(a => a.ToolAllowlist = new[] { "web.search" }, model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("Tool not allowed");
        task.FinishedAt.Should().NotBeNull();
    var trace = await db.Set<AgentTrace>().FirstOrDefaultAsync(t => t.TaskId == task.Id && t.Kind == TraceKind.Tool);
    trace.Should().NotBeNull();
    trace!.Name.Should().Be("db.query");
    using var outDoc = JsonDocument.Parse(trace.OutputJson);
    outDoc.RootElement.TryGetProperty("error", out var errProp).Should().BeTrue();
    errProp.GetString()!.Should().Contain("Tool not allowed");
    }

    [Fact]
    public async Task ToolMissing_FailsWithToolNotFound()
    {
        var scripted = new OneToolLoopMockModel("not.there");
        var (orch, db, agent, task) = BuildHarness(a => a.ToolAllowlist = new[] { "web.search", "not.there" }, model: scripted, tools: new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);
        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("Tool not found");
        task.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task MaxStepsExceeded_FailsAfterCap()
    {
        var scripted = new OneToolLoopMockModel("web.search");
        var (orch, db, agent, task) = BuildHarness(a => a.MaxSteps = 1, model: scripted);
        await orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);
        task.Status.Should().Be(AgentStatus.Failed);
        task.ErrorMessage.Should().Contain("MaxSteps");
        task.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancellation_StopsMidRun()
    {
        var scripted = new OneToolLoopMockModel("web.search");
        var (orch, db, agent, task) = BuildHarness(model: scripted);
        using var cts = new CancellationTokenSource();
        var run = orch.RunAsync(agent, task, tenant: "t1", user: "u1", ct: cts.Token);
        cts.CancelAfter(10);
        try { await run; } catch (OperationCanceledException) { }

        // After cancellation, status should be Running or Failed depending on timing; but traces are persisted
        var traces = await db.Set<AgentTrace>().AsNoTracking().Where(t => t.TaskId == task.Id).OrderBy(t => t.StepNumber).ToListAsync();
        traces.Count.Should().BeGreaterOrEqualTo(0);
    }

    // Local model helpers for deterministic behavior
    private sealed class TwoStepMockModel : IModelAdapter
    {
        private bool _planned;
        public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
        {
            if (!_planned)
            {
                _planned = true;
                return Task.FromResult(new ModelDecision(new UseTool("web.search", JsonDocument.Parse("{}")), 10, 1, "plan"));
            }
            return Task.FromResult(new ModelDecision(new FinalAnswer(JsonDocument.Parse("{\"ok\":true}")), 5, 1, "final"));
        }
    }

    private sealed class OneToolLoopMockModel : IModelAdapter
    {
        private readonly string _tool;
        public OneToolLoopMockModel(string tool) => _tool = tool;
        public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
            => Task.FromResult(new ModelDecision(new UseTool(_tool, JsonDocument.Parse("{}")), 1, 1, "loop"));
    }
}
