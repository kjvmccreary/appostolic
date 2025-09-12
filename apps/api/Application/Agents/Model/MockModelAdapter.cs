using System.Text.Json;
using Appostolic.Api.Application.Agents.Runtime;

namespace Appostolic.Api.Application.Agents.Model;

/// <summary>
/// Deterministic mock model adapter with no external calls.
/// Decides based on context keys: { next:"tool", tool:"name", input:{...} } or { final:{...} }.
/// Defaults to UseTool("web.search", { q:"intro", take:3 }).
/// </summary>
public sealed class MockModelAdapter : IModelAdapter
{
    public Task<ModelDecision> DecideAsync(ModelPrompt prompt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = prompt.Context?.RootElement ?? default;

        PlanAction action;
        if (ctx.ValueKind == JsonValueKind.Object)
        {
            // If explicit final
            if (ctx.TryGetProperty("final", out var finalElem) && finalElem.ValueKind != JsonValueKind.Undefined && finalElem.ValueKind != JsonValueKind.Null)
            {
                var finalDoc = Clone(finalElem);
                action = new FinalAnswer(finalDoc);
            }
            // If we've executed a tool, finish with its output as the final answer
            else if (ctx.TryGetProperty("lastTool", out var lastTool) && lastTool.ValueKind == JsonValueKind.Object)
            {
                if (lastTool.TryGetProperty("output", out var outElem) && outElem.ValueKind != JsonValueKind.Undefined && outElem.ValueKind != JsonValueKind.Null)
                {
                    var finalDoc = Clone(outElem);
                    action = new FinalAnswer(finalDoc);
                }
                else
                {
                    action = new FinalAnswer(JsonDocument.Parse("{}"));
                }
            }
            // If explicit tool call
            else if (ctx.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String && next.GetString()?.Equals("tool", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (ctx.TryGetProperty("tool", out var toolNameElem) && toolNameElem.ValueKind == JsonValueKind.String)
                {
                    var toolName = toolNameElem.GetString() ?? "web.search";
                    JsonDocument toolInput = JsonDocument.Parse("{}");
                    if (ctx.TryGetProperty("input", out var inputElem) && inputElem.ValueKind != JsonValueKind.Undefined && inputElem.ValueKind != JsonValueKind.Null)
                    {
                        toolInput = Clone(inputElem);
                    }
                    action = new UseTool(toolName, toolInput);
                }
                else
                {
                    action = new UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":3}"));
                }
            }
            else
            {
                action = new UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":3}"));
            }
        }
        else
        {
            action = new UseTool("web.search", JsonDocument.Parse("{\"q\":\"intro\",\"take\":3}"));
        }

        var contextLen = prompt.Context is null ? 0 : prompt.Context.RootElement.GetRawText().Length;
    var promptTokens = 100 + (contextLen / 50);
        var completionTokens = 20;
        var rationale = action is FinalAnswer ? "Final answer given from context.final" : "Planned tool use from context or default";

        var decision = new ModelDecision(action, promptTokens, completionTokens, rationale);
        return Task.FromResult(decision);
    }

    private static JsonDocument Clone(JsonElement element)
    {
        using var s = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(s))
        {
            element.WriteTo(writer);
        }
        return JsonDocument.Parse(s.ToArray());
    }
}
