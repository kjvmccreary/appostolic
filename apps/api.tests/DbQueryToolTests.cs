using System.Text.Json;
using System.Linq;
using Appostolic.Api.Application.Agents.Tools;
using Microsoft.Extensions.Logging.Abstractions;

public class DbQueryToolTests
{
    [Fact]
    public async Task Select_users_all_rows()
    {
    var tool = new DbQueryTool(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbQueryTool>());
        var input = JsonDocument.Parse(
            """{"sql":"SELECT id, email FROM users"}""");
        var req = new ToolCallRequest(tool.Name, input);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
        var res = await tool.InvokeAsync(req, ctx, default);

        res.Success.Should().BeTrue();
        res.Output.Should().NotBeNull();
    var results = res.Output!.RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Count.Should().BeGreaterThan(0);
        results[0].GetProperty("email").GetString().Should().NotBeNull();
    }

    [Fact]
    public async Task Select_lessons_where_id_param_object()
    {
    var tool = new DbQueryTool(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbQueryTool>());
        var input = JsonDocument.Parse(
            """{"sql":"SELECT * FROM lessons WHERE id = ?", "params": { "id": 2 }}""");
        var req = new ToolCallRequest(tool.Name, input);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
        var res = await tool.InvokeAsync(req, ctx, default);

        res.Success.Should().BeTrue();
    var results = res.Output!.RootElement.GetProperty("results").EnumerateArray().ToList();
        results.Count.Should().Be(1);
        results[0].GetProperty("title").GetString().Should().Be("Basics");
    }

    [Fact]
    public async Task Reject_non_select()
    {
    var tool = new DbQueryTool(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbQueryTool>());
        var input = JsonDocument.Parse(
            """{"sql":"UPDATE users SET email='x'"}""");
        var req = new ToolCallRequest(tool.Name, input);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
        var res = await tool.InvokeAsync(req, ctx, default);

        res.Success.Should().BeFalse();
        res.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Unknown_table_fails()
    {
        var tool = new DbQueryTool(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DbQueryTool>());
        var input = JsonDocument.Parse(
            """{"sql":"SELECT * FROM not_a_table"}""");
        var req = new ToolCallRequest(tool.Name, input);
        var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
        var res = await tool.InvokeAsync(req, ctx, default);

        res.Success.Should().BeFalse();
        res.Error.Should().Contain("Unknown fixture");
    }
}
