using System.Threading.Channels;
using Appostolic.Api.Application.Agents.Runtime;
using Appostolic.Api.Application.Agents;
using Appostolic.Api.Domain.Agents;
using Appostolic.Api.Application.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Agents.Queue;

public sealed class AgentTaskWorker : BackgroundService
{
    private readonly ChannelReader<Guid> _reader;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentTaskWorker> _logger;
    private long _dequeuedCount;
    private Guid? _inFlightTaskId;

    public AgentTaskWorker(InMemoryAgentTaskQueue queue, IServiceProvider services, ILogger<AgentTaskWorker> logger)
    {
        _reader = queue.Reader;
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentTaskWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid taskId;
            try
            {
                taskId = await _reader.ReadAsync(stoppingToken);
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
            var agentStore = sp.GetRequiredService<AgentStore>();
            var log = sp.GetRequiredService<ILogger<AgentTaskWorker>>();

            using (log.BeginScope(new Dictionary<string, object>
            {
                ["taskId"] = taskId
            }))
            {
                _inFlightTaskId = taskId;
                try
                {
                    // Load the task with a brief retry to tolerate request tx not yet committed
                    AgentTask? task = null;
                    const int maxLoadAttempts = 5;
                    for (var attempt = 0; attempt < maxLoadAttempts && task is null; attempt++)
                    {
                        task = await db.Set<AgentTask>().FirstOrDefaultAsync(t => t.Id == taskId, stoppingToken);
                        if (task is null)
                        {
                            if (attempt < maxLoadAttempts - 1)
                            {
                                try { await Task.Delay(50, stoppingToken); } catch (OperationCanceledException) { }
                            }
                        }
                    }
                    if (task is null)
                    {
                        log.LogWarning("AgentTask {TaskId} not found after retries. Skipping.", taskId);
                        _inFlightTaskId = null;
                        continue;
                    }

                    if (task.GuardrailDecision is GuardrailDecision.Deny or GuardrailDecision.Escalate)
                    {
                        if (task.Status == AgentStatus.Pending)
                        {
                            task.Status = AgentStatus.Failed;
                            task.ErrorMessage ??= $"Guardrail {task.GuardrailDecision.Value.ToString().ToLowerInvariant()}";
                            task.FinishedAt ??= DateTime.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                        }

                        log.LogInformation("AgentTask {TaskId} skipped due to guardrail decision {Decision}.", taskId, task.GuardrailDecision);
                        _inFlightTaskId = null;
                        continue;
                    }

                    using (log.BeginScope(new Dictionary<string, object>
                    {
                        ["agentId"] = task.AgentId
                    }))
                    {
                        // Idempotency: only process Pending tasks (refresh from DB to avoid racing with cancel)
                        if (task.Status != AgentStatus.Pending)
                        {
                            log.LogDebug("AgentTask {TaskId} has status {Status}; skipping.", taskId, task.Status);
                            continue;
                        }

                        // Re-read to ensure no concurrent transition (e.g., cancel) just occurred
                        await db.Entry(task).ReloadAsync(stoppingToken);
                        if (task.Status != AgentStatus.Pending)
                        {
                            log.LogDebug("AgentTask {TaskId} status changed to {Status} before start; skipping.", taskId, task.Status);
                            continue;
                        }

                        // Transition to Running
                        task.Status = AgentStatus.Running;
                        task.StartedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);

                        // Resolve agent via AgentStore (DB first, then registry fallback)
                        var agent = await agentStore.GetAsync(task.AgentId, stoppingToken);
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

                        using (var activity = Runtime.Telemetry.AgentSource.StartActivity("agent.run", System.Diagnostics.ActivityKind.Internal))
                        {
                            activity?.SetTag("task.id", taskId);
                            activity?.SetTag("agent.id", task.AgentId);
                            activity?.SetTag("tenant", tenant);
                            activity?.SetTag("user", user);
                            // Set baggage for downstream spans if any
                            System.Diagnostics.Activity.Current?.AddBaggage("tenant", tenant);
                            System.Diagnostics.Activity.Current?.AddBaggage("user", user);
                            using var runScope = log.BeginScope(new Dictionary<string, object?>
                            {
                                ["taskId"] = taskId,
                                ["agentId"] = task.AgentId,
                                ["tenant"] = tenant,
                                ["user"] = user,
                                ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString()
                            });
                            int attempts = 0;
                            while (!stoppingToken.IsCancellationRequested)
                            {
                                try
                                {
                                    await orchestrator.RunAsync(agent, task, tenant, user, stoppingToken);
                                    await db.SaveChangesAsync(stoppingToken);
                                    break; // success
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
                                    var transient = IsTransient(runEx);
                                    if (attempts == 0 && transient && !stoppingToken.IsCancellationRequested)
                                    {
                                        attempts++;
                                        log.LogWarning(runEx, "Transient error during orchestrator run (attempt {Attempt}/2). Retrying in 250ms.", attempts);
                                        try { await Task.Delay(250, stoppingToken); } catch (OperationCanceledException) { /* loop will exit */ }
                                        continue;
                                    }

                                    // Fail the task on unhandled or persistent errors
                                    task.Status = AgentStatus.Failed;
                                    task.ErrorMessage = Truncate(runEx.Message, 500);
                                    task.FinishedAt = DateTime.UtcNow;
                                    await db.SaveChangesAsync(stoppingToken);
                                    log.LogError(runEx, "AgentTask {TaskId} execution error after {Attempts} attempts.", taskId, attempts + 1);
                                    break;
                                }
                            }
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

    private static bool IsTransient(Exception ex)
    {
        // Treat timeouts and common transient DB keywords as transient.
        if (ex is TimeoutException) return true;

        if (ex is Microsoft.EntityFrameworkCore.DbUpdateException duex)
        {
            var im = duex.InnerException?.Message;
            if (ContainsTransientKeywords(im)) return true;
        }

        return ContainsTransientKeywords(ex.Message);
    }

    private static bool ContainsTransientKeywords(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var m = s.ToLowerInvariant();
        return m.Contains("timeout") || m.Contains("deadlock") || m.Contains("temporar") || m.Contains("could not serialize access");
    }
}
