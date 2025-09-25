using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Story 7: Validates that Grafana dashboard PromQL queries and Prometheus alert rules only reference
/// metrics that correspond to known auth instruments (dot names -> underscore names) or their
/// expected histogram suffix forms (_bucket, _sum, _count).
/// This functions as a lightweight lint to catch drift / typos without needing a full PromQL parser.
/// </summary>
public class DashboardMetricsValidationTests
{
    private static readonly string[] KnownBaseMetrics = new[]
    {
        // Counters
        "auth_tokens_issued",
        "auth_refresh_rotations",
        "auth_refresh_reuse_denied",
        "auth_refresh_expired",
        "auth_refresh_plaintext_emitted",
        "auth_refresh_plaintext_suppressed",
        "auth_logout_single",
        "auth_logout_all",
        "auth_login_success",
        "auth_login_failure",
        "auth_refresh_success",
        "auth_refresh_failure",
        "auth_refresh_rate_limited",
        "auth_jwt_key_rotation_tokens_signed",
        "auth_jwt_key_rotation_validation_failure",
        "auth_trace_enriched_spans",
        "auth_security_events_emitted",
        "auth_token_version_cache_hit",
        "auth_token_version_cache_miss",
        // Histograms (base names; exporter yields _sum/_count/_bucket)
        "auth_login_duration_ms",
        "auth_refresh_duration_ms",
        "auth_refresh_limiter_evaluation_ms",
        "auth_token_validation_latency_ms"
    };

    private static readonly Regex MetricRegex = new("auth_[a-z0-9_]+", RegexOptions.Compiled);

    [Fact]
    public void DashboardQueries_And_AlertRules_Use_Known_Metrics()
    {
        var root = FindRepoRoot();
        var dashboardDir = Path.Combine(root, "infra", "grafana", "dashboards");
        var alertsFile = Path.Combine(root, "infra", "alerts", "auth-rules.yml");
        File.Exists(alertsFile).Should().BeTrue("alerts file should exist at expected path");
        Directory.Exists(dashboardDir).Should().BeTrue("dashboard directory should exist");

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(dashboardDir, "*.json"))
        {
            var json = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(json);
            // Extract all expressions under panels[*].targets[*].expr
            if (doc.RootElement.TryGetProperty("panels", out var panels))
            {
                foreach (var panel in panels.EnumerateArray())
                {
                    if (panel.TryGetProperty("targets", out var targets))
                    {
                        foreach (var tgt in targets.EnumerateArray())
                        {
                            if (tgt.TryGetProperty("expr", out var exprProp))
                            {
                                var expr = exprProp.GetString() ?? string.Empty;
                                CaptureMetrics(expr, referenced);
                            }
                        }
                    }
                }
            }
        }

        // Alert rules (simple scan)
        var alertsText = File.ReadAllText(alertsFile);
        CaptureMetrics(alertsText, referenced);

        // Validate each referenced metric maps to a known base metric (allowing histogram suffixes)
        var knownSet = new HashSet<string>(KnownBaseMetrics, StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        foreach (var m in referenced)
        {
            var baseName = StripHistogramSuffix(m);
            if (!knownSet.Contains(baseName))
            {
                unknown.Add(m);
            }
        }

        unknown.Should().BeEmpty("all referenced metrics should correspond to declared auth instruments (unknown: {0})", string.Join(", ", unknown));
    }

    private static void CaptureMetrics(string text, HashSet<string> sink)
    {
        foreach (Match match in MetricRegex.Matches(text))
        {
            sink.Add(match.Value);
        }
    }

    private static string StripHistogramSuffix(string metric)
    {
        if (metric.EndsWith("_bucket", StringComparison.OrdinalIgnoreCase)) return metric.Substring(0, metric.Length - 7);
        if (metric.EndsWith("_sum", StringComparison.OrdinalIgnoreCase)) return metric.Substring(0, metric.Length - 4);
        if (metric.EndsWith("_count", StringComparison.OrdinalIgnoreCase)) return metric.Substring(0, metric.Length - 6);
        return metric;
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 10; i++)
        {
            if (Directory.GetFiles(dir, "appostolic.sln").Any()) return dir;
            dir = Directory.GetParent(dir)!.FullName;
        }
        throw new InvalidOperationException("Could not locate repository root (appostolic.sln not found)");
    }
}
