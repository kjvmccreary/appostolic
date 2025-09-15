using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Deterministic in-memory database query tool. Accepts only simple
/// "SELECT ... FROM &lt;fixture&gt; [WHERE id = ?]" queries and returns rows
/// from built-in fixtures. No external DB calls.
/// </summary>
public sealed class DbQueryTool : ITool
{
    /// <inheritdoc />
    public string Name => "db.query";

    private readonly ILogger<DbQueryTool> _logger;

    public DbQueryTool(ILogger<DbQueryTool> logger)
    {
        _logger = logger;
    }

    private static readonly UserRow[] Users = new[]
    {
        new UserRow(1, "a@example.com"),
        new UserRow(2, "b@example.com"),
        new UserRow(3, "c@example.com"),
    };

    private static readonly LessonRow[] Lessons = new[]
    {
        new LessonRow(1, "Intro"),
        new LessonRow(2, "Basics"),
        new LessonRow(3, "Advanced"),
    };

    // Very simple regex to capture table and optional WHERE
    private static readonly Regex SelectRegex = new(
        pattern: "^\\s*SELECT\\s+.+?\\s+FROM\\s+(?<table>[a-zA-Z_][a-zA-Z0-9_]*)\\s*(?:WHERE\\s+(?<where>.+))?;?\\s*$",
        options: RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public Task<ToolCallResult> InvokeAsync(ToolCallRequest request, ToolExecutionContext ctx, CancellationToken ct)
    {
        using var scope = ToolTelemetry.Start(ctx, Name, _logger);
        try
        {
            var input = request.ReadInput<QueryInput>();
            var sql = input.Sql?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sql))
            {
                return Task.FromResult(new ToolCallResult(false, null, "sql is required", scope.Complete(false)));
            }

            var m = SelectRegex.Match(sql);
            if (!m.Success || !sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new ToolCallResult(false, null, "Only SELECT ... FROM <fixture> queries are allowed", scope.Complete(false)));
            }

            var table = m.Groups["table"].Value;
            var where = m.Groups["where"].Success ? m.Groups["where"].Value.Trim() : null;

            int? idFilter = null;
            if (!string.IsNullOrWhiteSpace(where))
            {
                // Support only: id = ?
                if (!Regex.IsMatch(where, "^id\\s*=\\s*\\?$", RegexOptions.IgnoreCase))
                {
                    return Task.FromResult(new ToolCallResult(false, null, "Only WHERE id = ? is supported", scope.Complete(false)));
                }

                // Extract first positional parameter as id
                idFilter = ReadIdParam(input.Params);
                if (idFilter is null)
                {
                    return Task.FromResult(new ToolCallResult(false, null, "Missing positional parameter for id", scope.Complete(false)));
                }
            }

            object[] results;
            switch (table.ToLowerInvariant())
            {
                case "users":
                    var rowsU = Users.AsEnumerable();
                    if (idFilter is int uid)
                        rowsU = rowsU.Where(r => r.Id == uid);
                    results = rowsU.Cast<object>().ToArray();
                    break;
                case "lessons":
                    var rowsL = Lessons.AsEnumerable();
                    if (idFilter is int lid)
                        rowsL = rowsL.Where(r => r.Id == lid);
                    results = rowsL.Cast<object>().ToArray();
                    break;
                default:
                    return Task.FromResult(new ToolCallResult(false, null, $"Unknown fixture '{table}'", scope.Complete(false)));
            }

            _logger.LogInformation("db.query table={Table} rows={Count}", table, results.Length);

            var output = new { results };
            var json = JsonSerializer.SerializeToUtf8Bytes(output, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            });
            var doc = JsonDocument.Parse(json);
            return Task.FromResult(new ToolCallResult(true, doc, null, scope.Complete(true)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(new ToolCallResult(false, null, "canceled", scope.Complete(false)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "db.query failed");
            return Task.FromResult(new ToolCallResult(false, null, ex.Message, scope.Complete(false)));
        }
    }

    private static int? ReadIdParam(JsonElement? elem)
    {
        if (elem is null || elem.Value.ValueKind == JsonValueKind.Undefined || elem.Value.ValueKind == JsonValueKind.Null)
            return null;
        var v = elem.Value;
        if (v.ValueKind == JsonValueKind.Array)
        {
            if (v.GetArrayLength() == 0) return null;
            var first = v[0];
            if (first.ValueKind == JsonValueKind.Number && first.TryGetInt32(out var idA))
                return idA;
            if (first.ValueKind == JsonValueKind.String && int.TryParse(first.GetString(), out var idAs))
                return idAs;
            return null;
        }
        if (v.ValueKind == JsonValueKind.Object)
        {
            if (v.TryGetProperty("id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var idO))
                    return idO;
                if (idProp.ValueKind == JsonValueKind.String && int.TryParse(idProp.GetString(), out var idOs))
                    return idOs;
            }
            return null;
        }
        return null;
    }

    private readonly record struct QueryInput(string Sql, JsonElement? Params);

    private readonly record struct UserRow(int Id, string Email);

    private readonly record struct LessonRow(int Id, string Title);
}
