using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Deterministic in-memory implementation of a web search tool.
/// No external network calls; filters a static fixture by query.
/// </summary>
public sealed class WebSearchTool : ITool
{
    private readonly ILogger<WebSearchTool> _logger;

    public WebSearchTool(ILogger<WebSearchTool> logger)
    {
        _logger = logger;
    }
    /// <inheritdoc />
    public string Name => "web.search";

    private static readonly Item[] Fixture = new[]
    {
    new Item("Intro to EF Core", "https://learn.microsoft.com/ef/core/introduction", "Intro and basics for Entity Framework Core."),
        new Item("EF Core Value Conversions", "https://learn.microsoft.com/ef/core/modeling/value-conversions", "Map .NET types to database representations using converters."),
        new Item("ASP.NET Core Minimal APIs", "https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis", "Build HTTP APIs with minimal dependencies and boilerplate."),
        new Item("Npgsql & PostgreSQL", "https://www.npgsql.org/efcore/", "Entity Framework Core provider for PostgreSQL with Npgsql."),
        new Item("System.Text.Json", "https://learn.microsoft.com/dotnet/standard/serialization/system-text-json-overview", "High-performance JSON APIs in .NET for serialization/deserialization."),
        new Item("Swashbuckle for ASP.NET Core", "https://github.com/domaindrivendev/Swashbuckle.AspNetCore", "Generate Swagger/OpenAPI documents and UI for ASP.NET Core."),
        new Item("C# Records Overview", "https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record", "Reference types with value-like semantics for immutable models."),
        new Item("LINQ Guide", "https://learn.microsoft.com/dotnet/csharp/linq/", "Query and transform data collections with LINQ."),
        new Item("Logging in .NET", "https://learn.microsoft.com/dotnet/core/extensions/logging", "Structured logging abstractions with ILogger in .NET."),
    };

    /// <inheritdoc />
    public Task<ToolCallResult> InvokeAsync(ToolCallRequest request, ToolExecutionContext ctx, CancellationToken ct)
    {
    using var scope = ToolTelemetry.Start(ctx, Name, _logger);
        try
        {
            var input = request.ReadInput<SearchInput>();
            var q = input.Q?.Trim() ?? string.Empty;
            var take = input.Take ?? 5;
            if (take < 1) take = 1; if (take > 50) take = 50;

            Item[] hits;
            if (string.IsNullOrWhiteSpace(q))
            {
                hits = Array.Empty<Item>();
            }
            else
            {
                hits = Fixture
                    .Where(i =>
                        i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.Snippet.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        i.Url.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(take)
                    .ToArray();
            }

            _logger.LogInformation("web.search q=\"{Query}\" hits={HitCount}", q, hits.Length);

            var payload = new SearchOutput(hits.Select(h => new SearchOutputItem(h.Title, h.Url, h.Snippet)).ToArray());
            var json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            var doc = JsonDocument.Parse(json);
            return Task.FromResult(new ToolCallResult(true, doc, null, scope.Complete(true)));
        }
        catch (OperationCanceledException)
        {
            // Surface cancellations as non-success with empty error to avoid throwing upstream; deterministic v1.
            return Task.FromResult(new ToolCallResult(false, null, "canceled", scope.Complete(false)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "web.search failed");
            return Task.FromResult(new ToolCallResult(false, null, ex.Message, scope.Complete(false)));
        }
    }

    private readonly record struct Item(string Title, string Url, string Snippet);

    private readonly record struct SearchInput(string? Q, int? Take);

    private readonly record struct SearchOutput(SearchOutputItem[] Results);

    private readonly record struct SearchOutputItem(string Title, string Url, string Snippet);
}
