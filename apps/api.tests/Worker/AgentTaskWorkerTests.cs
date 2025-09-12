using System.Text.Json;
using Appostolic.Api.Application.Agents.Model;
using Appostolic.Api.Application.Agents.Queue;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.Application.Agents;
using Appostolic.Api.Domain.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Appostolic.Api.Tests.Worker;

public class AgentTaskWorkerTests
{
    [Fact]
    public async Task Pending_To_Running_To_Succeeded_With_Traces()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
        services.AddSingleton(new InMemoryDatabaseRoot());
        services.AddDbContext<AppDbContext>((sp, o) => o.UseInMemoryDatabase("workerdb-shared", sp.GetRequiredService<InMemoryDatabaseRoot>()));

                services.AddSingleton<ITool, WebSearchTool>();
                services.AddSingleton<ToolRegistry>();
                services.AddScoped<AgentStore>();

                services.AddScoped<ITraceWriter, TraceWriter>();
                services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
                // Use a scripted model adapter that plans one tool call then finalizes
                services.AddSingleton<IModelAdapter, PlanThenFinalModelAdapter>();

                services.AddSingleton<InMemoryAgentTaskQueue>();
                services.AddSingleton<IAgentTaskQueue>(sp => sp.GetRequiredService<InMemoryAgentTaskQueue>());
                services.AddHostedService<AgentTaskWorker>();
            })
            .Build();

        await host.StartAsync(cts.Token);

        var sp = host.Services;
        var db = sp.GetRequiredService<AppDbContext>();
        var queue = sp.GetRequiredService<InMemoryAgentTaskQueue>();

        // Seed a Pending task for ResearchAgent
        var agent = Application.Agents.AgentRegistry.FindById(Application.Agents.AgentRegistry.ResearchAgentId)!;
        var task = new AgentTask(Guid.NewGuid(), agent.Id, JsonSerializer.Serialize(new { topic = "intro" }))
        {
            Status = AgentStatus.Pending,
            RequestTenant = "kevin-personal",
            RequestUser = "kevin@example.com"
        };
        db.Add(task);
        await db.SaveChangesAsync(cts.Token);

        await queue.EnqueueAsync(task.Id, cts.Token);

        // Await terminal state
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            var t = await db.Set<AgentTask>().AsNoTracking().FirstAsync(x => x.Id == task.Id, cts.Token);
            if (t.Status is AgentStatus.Succeeded or AgentStatus.Failed or AgentStatus.Canceled)
            {
                task = t;
                break;
            }
            await Task.Delay(100, cts.Token);
        }

        task.Status.Should().Be(AgentStatus.Succeeded);
        task.StartedAt.Should().NotBeNull();
        task.FinishedAt.Should().NotBeNull();

        var traces = await db.Set<AgentTrace>().AsNoTracking().Where(tr => tr.TaskId == task.Id).OrderBy(tr => tr.StepNumber).ToListAsync(cts.Token);
        traces.Count.Should().BeGreaterOrEqualTo(2);
        traces[0].Kind.Should().Be(TraceKind.Model);
        traces[1].Kind.Should().Be(TraceKind.Tool);

        await host.StopAsync(TimeSpan.FromSeconds(2));
    }
}

// Scripted model: first Decide -> UseTool("web.search"), second -> FinalAnswer
file sealed class PlanThenFinalModelAdapter : IModelAdapter
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
