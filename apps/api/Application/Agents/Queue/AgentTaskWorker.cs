using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Agents.Queue;

public sealed class AgentTaskWorker : BackgroundService
{
    private readonly IAgentTaskQueue _queue;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentTaskWorker> _logger;
    private long _dequeuedCount;
    private Guid? _inFlightTaskId;

    public AgentTaskWorker(IAgentTaskQueue queue, IServiceProvider services, ILogger<AgentTaskWorker> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // We only support the in-memory queue in Development for now.
        if (_queue is not InMemoryAgentTaskQueue inMemory)
        {
            _logger.LogWarning("AgentTaskWorker: Queue implementation is not in-memory; worker idle.");
            return;
        }

        _logger.LogInformation("AgentTaskWorker started.");

    var reader = inMemory.Reader;
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid taskId;
            try
            {
        taskId = await reader.ReadAsync(stoppingToken);
        Interlocked.Increment(ref _dequeuedCount);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AgentTaskWorker: Error reading from queue");
                await Task.Delay(250, stoppingToken);
                continue;
            }

            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();
            var orchestrator = sp.GetRequiredService<IAgentOrchestrator>();
            var log = sp.GetRequiredService<ILogger<AgentTaskWorker>>();

            using (log.BeginScope(new Dictionary<string, object>
            {
                ["taskId"] = taskId
            }))
            {
                _inFlightTaskId = taskId;
                try
                {
                    var task = await db.Set<AgentTask>().FirstOrDefaultAsync(t => t.Id == taskId, stoppingToken);
                    if (task is null)
                    {
                        log.LogWarning("AgentTask {TaskId} not found. Skipping.", taskId);
                        _inFlightTaskId = null;
                        continue;
                    }

                    using (log.BeginScope(new Dictionary<string, object>
                    {
                        ["agentId"] = task.AgentId
                    }))
                    {
                        // Idempotency: only process Pending tasks
                        if (task.Status != AgentStatus.Pending)
                        {
                            log.LogDebug("AgentTask {TaskId} has status {Status}; skipping.", taskId, task.Status);
                            continue;
                        }

                        // Transition to Running
                        task.Status = AgentStatus.Running;
                        task.StartedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);

                        // Resolve agent from registry
                        var agent = Application.Agents.AgentRegistry.FindById(task.AgentId);
                        if (agent is null)
                        {
                            task.Status = AgentStatus.Failed;
                            task.ErrorMessage = "Agent not found";
                            task.FinishedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                            log.LogWarning("Agent {AgentId} not found for task {TaskId}.", task.AgentId, taskId);
                            continue;
                        }

                        // Request context fallbacks for development
                        var tenant = task.RequestTenant ?? "dev";
                        var user = task.RequestUser ?? "dev";

                        try
                        {
                            await orchestrator.RunAsync(agent, task, tenant, user, stoppingToken);
                            await db.SaveChangesAsync(stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            // On shutdown: mark task as Canceled if still Running
                            if (task.Status == AgentStatus.Running)
                            {
                                task.Status = AgentStatus.Canceled;
                                task.ErrorMessage = "Canceled";
                                task.FinishedAt = DateTime.UtcNow;
                                try { await db.SaveChangesAsync(stoppingToken); }
                                catch (OperationCanceledException)
                                {
                                    // Final attempt without cancellation to persist terminal state
                                    try { await db.SaveChangesAsync(CancellationToken.None); } catch { /* swallow */ }
                                }
                            }
                            throw;
                        }
                        catch (Exception runEx)
                        {
                            // Fail the task on unhandled errors
                            task.Status = AgentStatus.Failed;
                            task.ErrorMessage = Truncate(runEx.Message, 500);
                            task.FinishedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                            log.LogError(runEx, "AgentTask {TaskId} execution error.", taskId);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AgentTaskWorker: Fatal error processing task {TaskId}.", taskId);
                }
                finally
                {
                    _inFlightTaskId = null;
                }
            }
        }

        var count = Interlocked.Read(ref _dequeuedCount);
        _logger.LogInformation("AgentTaskWorker stopping. dequeued={Count} inflight={HasInFlight} inflightTaskId={TaskId}", count, _inFlightTaskId.HasValue, _inFlightTaskId);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value.Substring(0, max));
}
