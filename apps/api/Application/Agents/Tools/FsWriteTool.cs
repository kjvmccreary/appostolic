using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appostolic.Api.App.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Appostolic.Api.Application.Agents.Tools;

/// <summary>
/// Safe filesystem write tool. Writes UTF-8 (no BOM) files under a configured root.
/// Enforces size limit and prevents path traversal outside of FsRoot.
/// </summary>
public sealed class FsWriteTool : ITool
{
    public string Name => "fs.write";

    private readonly IOptions<ToolsOptions> _options;
    private readonly ILogger<FsWriteTool> _logger;

    public FsWriteTool(IOptions<ToolsOptions> options, ILogger<FsWriteTool> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task<ToolCallResult> InvokeAsync(ToolCallRequest request, ToolExecutionContext ctx, CancellationToken ct)
    {
    using var scope = ToolTelemetry.Start(ctx, Name, _logger);
        try
        {
            var input = request.ReadInput<InputDto>();
            if (string.IsNullOrWhiteSpace(input.Path))
        return Task.FromResult(new ToolCallResult(false, null, "path is required", scope.Complete(false)));
            if (input.Content is null)
        return Task.FromResult(new ToolCallResult(false, null, "content is required", scope.Complete(false)));

            var opts = _options.Value ?? new ToolsOptions();
            var root = string.IsNullOrWhiteSpace(opts.FsRoot) ? "/tmp/appostolic" : opts.FsRoot!;
            var maxBytes = opts.MaxBytes > 0 ? opts.MaxBytes : 1_048_576; // default 1MB

            // Normalize paths and enforce root
            var rootFull = NormalizeDir(root);
            var relPath = input.Path.TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, relPath));
            if (!IsUnderRoot(fullPath, rootFull))
            {
                return Task.FromResult(new ToolCallResult(false, null, "path traversal detected; write denied", scope.Complete(false)));
            }

            // Size check
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var bytes = utf8NoBom.GetBytes(input.Content);
            if (bytes.Length > maxBytes)
            {
                return Task.FromResult(new ToolCallResult(false, null, $"content exceeds max bytes ({bytes.Length} > {maxBytes})", scope.Complete(false)));
            }

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(dir);

            // Write file
            using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
            }

            // Compute SHA-256
            string sha256Hex;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                sha256Hex = Convert.ToHexString(hash).ToLowerInvariant();
            }

            _logger.LogInformation("fs.write path={Path} bytes={Bytes}", fullPath, bytes.Length);

            var output = new { path = fullPath, bytes = bytes.Length, sha256 = sha256Hex };
            var json = JsonSerializer.SerializeToUtf8Bytes(output, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
            _logger.LogError(ex, "fs.write failed");
            return Task.FromResult(new ToolCallResult(false, null, ex.Message, scope.Complete(false)));
        }
    }

    private static string NormalizeDir(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar))
            full += System.IO.Path.DirectorySeparatorChar;
        return full;
    }

    private static bool IsUnderRoot(string fullPath, string rootFull)
    {
        var normalizedFull = System.IO.Path.GetFullPath(fullPath);
        return normalizedFull.StartsWith(rootFull, StringComparison.Ordinal);
    }

    private readonly record struct InputDto(string Path, string Content);
}
