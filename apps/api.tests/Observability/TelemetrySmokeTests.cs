using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.Domain.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Appostolic.Api.Tests.Observability;

public class TelemetrySmokeTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task HappyPath_Emits_Spans_And_Metrics()
    {
        using var db = CreateDb();

        var agent = new Agent(Guid.NewGuid(), "tester", "system", new[] { "web.search" }, model: "mock", temperature: 0.0, maxSteps: 8);
        db.Add(agent);
        var task = new AgentTask(Guid.NewGuid(), agent.Id, JsonSerializer.Serialize(new { q = "intro" }));
        db.Add(task);
        db.SaveChanges();

        var reg = new ToolRegistry(new ITool[] { new WebSearchTool(NullLogger<WebSearchTool>.Instance) });
        var trace = new TraceWriter(db, NullLogger<TraceWriter>.Instance);
    var orchestrator = new AgentOrchestrator(db, new PlanThenFinalModel(), reg, trace, NullLogger<AgentOrchestrator>.Instance, Microsoft.Extensions.Options.Options.Create(new Appostolic.Api.App.Options.ModelPricingOptions()));

        // Capture spans from our ActivitySources
        var spanNames = new List<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Appostolic.AgentRuntime" || source.Name == "Appostolic.Tools",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                lock (spanNames) spanNames.Add(activity.OperationName);
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        // Capture metrics from our Meter via MeterListener
        var measurements = new List<(string name, double value, IReadOnlyList<KeyValuePair<string, object?>> tags)>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Appostolic.Metrics")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((inst, val, tags, state) =>
        {
            lock (measurements) measurements.Add((inst.Name, val, tags.ToArray()));
        });
        meterListener.Start();

        // Simulate endpoint-created metric and run orchestrator to completion
        Metrics.RecordTaskCreated(tenant: "t1", agentId: agent.Id);
        await orchestrator.RunAsync(agent, task, tenant: "t1", user: "u1", ct: default);

        // Snapshot results under lock to avoid concurrent modification during enumeration
        List<string> spanSnapshot;
        lock (spanNames) spanSnapshot = spanNames.ToList();

        List<(string name, double value, IReadOnlyList<KeyValuePair<string, object?>> tags)> measurementsSnapshot;
        lock (measurements) measurementsSnapshot = measurements.ToList();

        // Assert spans: at least one model and one tool span observed
        spanSnapshot.Should().Contain("agent.model");
        spanSnapshot.Any(n => n.StartsWith("tool.")).Should().BeTrue();

        // Assert metrics: created and completed (Succeeded) observed
        var created = measurementsSnapshot.Any(m => m.name == "agent.tasks.created");
        created.Should().BeTrue();

        var completedSucceeded = measurementsSnapshot.Any(m =>
            m.name == "agent.tasks.completed" && m.tags.Any(t => t.Key == "status" && t.Value?.ToString() == AgentStatus.Succeeded.ToString()));
        completedSucceeded.Should().BeTrue();
    }

    // Scripted model: first Decide -> UseTool("web.search"), second -> FinalAnswer
    private sealed class PlanThenFinalModel : IModelAdapter
    {
        private int _calls;

        public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _calls++;
            if (_calls == 1)
            {
                return Task.FromResult(new ModelDecision(
                    new UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":1}")),
                    5, 1, "plan"));
            }
            return Task.FromResult(new ModelDecision(
                new FinalAnswer(JsonDocument.Parse("{\"ok\":true}")),
                3, 1, "final"));
        }
    }
}
