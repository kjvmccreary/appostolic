using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Appostolic.Api.App.Notifications;

public static class EmailMetrics
{
    private static readonly Counter<long> EmailsSent = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.sent.total", unit: "{email}", description: "Total emails successfully sent.");

    private static readonly Counter<long> EmailsFailed = Appostolic.Api.Application.Agents.Runtime.Metrics.Meter
        .CreateCounter<long>("email.failed.total", unit: "{email}", description: "Total emails that failed after retries.");

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
}
