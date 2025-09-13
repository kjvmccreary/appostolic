using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Appostolic.Api.App.Notifications;

public static class EmailMetrics
{
    private static readonly Counter<long> EmailsSent = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.sent.total", unit: "{email}", description: "Total emails successfully sent.");

    private static readonly Counter<long> EmailsFailed = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.failed.total", unit: "{email}", description: "Total emails that failed after retries.");

    // Notif-30: Resend metrics
    private static readonly Counter<long> EmailsResent = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.resend.total", unit: "{email}", description: "Total resends initiated, tagged by mode/outcome.");

    private static readonly Counter<long> EmailsResendThrottled = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.resend.throttled.total", unit: "{email}", description: "Total resends that were throttled by policy.");

    private static readonly Histogram<long> ResendBatchSize = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateHistogram<long>("email.resend.batch.size", unit: "{email}", description: "Batch size for bulk resend requests.");

    public static void RecordSent(string kind)
    {
        var tags = new TagList { { "kind", kind } };
        EmailsSent.Add(1, tags);
    }

    public static void RecordFailed(string kind)
    {
        var tags = new TagList { { "kind", kind } };
        EmailsFailed.Add(1, tags);
    }

    // Records a resend attempt outcome. Mode: manual|bulk. Outcome: created|throttled|forbidden|not_found|error
    // tenantScope: self|superadmin|dev (for dev endpoints)
    public static void RecordResend(string kind, string mode, string tenantScope, string outcome)
    {
        var tags = new TagList
        {
            { "kind", kind },
            { "mode", mode },
            { "tenant_scope", tenantScope },
            { "outcome", outcome }
        };
        EmailsResent.Add(1, tags);
        if (string.Equals(outcome, "throttled", StringComparison.OrdinalIgnoreCase))
        {
            EmailsResendThrottled.Add(1, tags);
        }
    }

    public static void RecordResendBatchSize(int size, string tenantScope, string? kind)
    {
        var tags = new TagList { { "tenant_scope", tenantScope } };
        if (!string.IsNullOrWhiteSpace(kind)) tags.Add("kind", kind!);
        ResendBatchSize.Record(size, tags);
    }
}
