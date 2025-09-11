using System.Text.Json;
using Appostolic.Api.Application.Agents.Tools;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public class FsWriteToolTests
{
    private IOptions<ToolsOptions> Options(string root, int maxBytes = 1048576)
        => Microsoft.Extensions.Options.Options.Create(new ToolsOptions { FsRoot = root, MaxBytes = maxBytes });

    [Fact]
    public async Task Writes_file_under_root_and_returns_sha()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "appostolic-tests", Guid.NewGuid().ToString("n"));
        try
        {
            var tool = new FsWriteTool(Options(tmp), new Microsoft.Extensions.Logging.Abstractions.NullLogger<FsWriteTool>());
            var input = JsonDocument.Parse(
                """{"path":"notes/hello.txt","content":"hello world"}""");
            var req = new ToolCallRequest(tool.Name, input);
            var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
            var res = await tool.InvokeAsync(req, ctx, default);

            res.Success.Should().BeTrue();
            var root = res.Output!.RootElement;
            var path = root.GetProperty("path").GetString()!;
            File.Exists(path).Should().BeTrue();
            root.GetProperty("bytes").GetInt32().Should().Be(11);
            var sha = root.GetProperty("sha256").GetString();
            sha.Should().NotBeNull();
            sha!.Length.Should().Be(64);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_when_content_exceeds_max()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "appostolic-tests", Guid.NewGuid().ToString("n"));
        try
        {
            var tool = new FsWriteTool(Options(tmp, maxBytes: 4), new Microsoft.Extensions.Logging.Abstractions.NullLogger<FsWriteTool>());
            var input = JsonDocument.Parse(
                """{"path":"a.txt","content":"12345"}""");
            var req = new ToolCallRequest(tool.Name, input);
            var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
            var res = await tool.InvokeAsync(req, ctx, default);

            res.Success.Should().BeFalse();
            res.Error.Should().Contain("exceeds max bytes");
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_path_traversal()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "appostolic-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);
        try
        {
            var tool = new FsWriteTool(Options(tmp), new Microsoft.Extensions.Logging.Abstractions.NullLogger<FsWriteTool>());
            var input = JsonDocument.Parse(
                """{"path":"../outside.txt","content":"x"}""");
            var req = new ToolCallRequest(tool.Name, input);
            var ctx = new ToolExecutionContext(Guid.NewGuid(), 1, "t", "u", NullLogger.Instance);
            var res = await tool.InvokeAsync(req, ctx, default);

            res.Success.Should().BeFalse();
            res.Error.Should().Contain("path traversal");
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
        }
    }
}
