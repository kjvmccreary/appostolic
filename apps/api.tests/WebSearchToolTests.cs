using System.Text.Json;
using Appostolic.Api.Application.Agents.Tools;
using Microsoft.Extensions.Logging.Abstractions;

public class WebSearchToolTests
{
    private static ToolExecutionContext Ctx() => new ToolExecutionContext(
        TaskId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        StepNumber: 1,
        Tenant: "test-tenant",
        User: "test@example.com",
        Logger: NullLogger.Instance);

    [Fact]
    public async Task Query_intro_returns_expected()
    {
        var tool = new WebSearchTool(new NullLogger<WebSearchTool>());
        var input = JsonDocument.Parse("""{"q":"intro"}""");
        var res = await tool.InvokeAsync(new ToolCallRequest(tool.Name, input), Ctx(), default);
        res.Success.Should().BeTrue();
        var results = res.Output!.RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Should().NotBeEmpty();
        results.Select(r => r.GetProperty("title").GetString()).Any(t => t!.Contains("Intro", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task Take_limits_results()
    {
        var tool = new WebSearchTool(new NullLogger<WebSearchTool>());
        var input = JsonDocument.Parse("""{"q":".net","take":1}""");
        var res = await tool.InvokeAsync(new ToolCallRequest(tool.Name, input), Ctx(), default);
        res.Success.Should().BeTrue();
        var results = res.Output!.RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Count.Should().Be(1);
    }

    [Fact]
    public async Task Empty_query_returns_empty()
    {
        var tool = new WebSearchTool(new NullLogger<WebSearchTool>());
        var input = JsonDocument.Parse("""{"q":""}""");
        var res = await tool.InvokeAsync(new ToolCallRequest(tool.Name, input), Ctx(), default);
        res.Success.Should().BeTrue();
        var results = res.Output!.RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Count.Should().Be(0);
    }
}
