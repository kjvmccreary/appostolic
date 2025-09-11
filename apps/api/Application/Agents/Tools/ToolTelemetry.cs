using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Helper to capture minimal telemetry for tool calls: Activity (if listeners),
/// elapsed time, and standard tags. Also emits a compact structured log event.
/// Tags: tool.name, tenant, user, duration.ms, success
/// </summary>
public static class ToolTelemetry
{
    private static readonly ActivitySource ActivitySource = new("Appostolic.Tools");

    public static Scope Start(ToolExecutionContext ctx, string toolName, ILogger logger)
        => new Scope(toolName, ctx.Tenant, ctx.User, logger);

    public sealed class Scope : IDisposable
    {
        private readonly string _toolName;
        private readonly string _tenant;
        private readonly string _user;
        private readonly ILogger _logger;
        private readonly Activity? _activity;
        private readonly Stopwatch _sw;

        internal Scope(string toolName, string tenant, string user, ILogger logger)
        {
            _toolName = toolName;
            _tenant = tenant;
            _user = user;
            _logger = logger;
            _activity = ActivitySource.StartActivity("tool.invoke", ActivityKind.Internal);
            _sw = Stopwatch.StartNew();
        }

        public int Complete(bool success)
        {
            if (_sw.IsRunning) _sw.Stop();
            var ms = (int)_sw.Elapsed.TotalMilliseconds;
            if (ms < 1) ms = 1; // ensure a minimum of 1ms for deterministic assertions
            _activity?.SetTag("tool.name", _toolName);
            _activity?.SetTag("tenant", _tenant);
            _activity?.SetTag("user", _user);
            _activity?.SetTag("duration.ms", ms);
            _activity?.SetTag("success", success);
            _logger.LogInformation("tool.call name={Name} tenant={Tenant} user={User} success={Success} durationMs={DurationMs}", _toolName, _tenant, _user, success, ms);
            _activity?.Stop();
            return ms;
        }

        public void Dispose()
        {
            if (_activity is { Duration: { } d } && d == TimeSpan.Zero)
            {
                _activity.Stop();
            }
        }
    }
}
