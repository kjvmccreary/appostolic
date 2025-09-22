using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Appostolic.Api.Tests.Guard;

/// <summary>
/// Guard test for RDH Story 2: fails if any usages of removed AuthTestClient.UseTenantAsync remain.
/// </summary>
public class NoUseTenantAsyncLeftTests
{
    // Allow list: only this guard test file is allowed to contain the literal search token
    // because the assertion itself references the deprecated method name.
    private static readonly string[] AllowList = new[] { nameof(NoUseTenantAsyncLeftTests) + ".cs" };

    [Fact]
    public void Detect_remaining_UseTenantAsync_usages()
    {
        var root = FindRepoRoot();
        var testRoot = Path.Combine(root, "apps", "api.tests");
        var files = Directory.GetFiles(testRoot, "*.cs", SearchOption.AllDirectories);
        var matches = files
            .Where(f => File.ReadAllText(f).Contains("AuthTestClient.UseTenantAsync"))
            .Where(f => !AllowList.Any(a => f.EndsWith(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var msg = matches.Count > 0 ? $"Found {matches.Count} remaining AuthTestClient.UseTenantAsync usages (excluding guard self-file):\n" + string.Join('\n', matches.Select(Path.GetFileName)) : null;
        Assert.True(matches.Count == 0, msg);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != "/" && !File.Exists(Path.Combine(dir, "appostolic.sln")))
        {
            dir = Directory.GetParent(dir)!.FullName;
        }
        return dir;
    }
}
