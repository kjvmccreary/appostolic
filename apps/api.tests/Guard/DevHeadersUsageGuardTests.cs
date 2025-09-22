using System.IO;
using System.Linq;
using Xunit;

namespace Appostolic.Api.Tests.Guard;

/// <summary>
/// Fails if any test file (outside the intentional negative-path allowlist) still contains
/// dev header strings ("x-dev-user" or "x-tenant"). This enforces RDH Story 2 Phase D completion.
/// Update allowlist ONLY when adding a deliberate negative-path regression test.
/// </summary>
public class DevHeadersUsageGuardTests
{
    private static readonly string[] AllowList =
    {
        // Negative-path / regression guard suites intentionally referencing legacy headers
        "Auth/DevHeadersDisabledTests.cs",
        "Auth/DevHeadersRemovedTests.cs"
    };

    [Fact]
    public void No_DevHeader_Strings_Remain_In_Tests_Outside_AllowList()
    {
        // Resolve test project root: ascend 3 levels from bin/Debug/net8.0 => api.tests
        // (Earlier version went up 4 levels causing scan to include sibling runtime projects.)
        var baseDir = AppContext.BaseDirectory; // .../apps/api.tests/bin/Debug/net8.0/
        var testRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        if (!File.Exists(Path.Combine(testRoot, "Appostolic.Api.Tests.csproj")))
        {
            throw new InvalidOperationException($"Could not locate test project root at expected path: {testRoot}");
        }
        Assert.True(Directory.Exists(testRoot), $"Test root not found: {testRoot}");

        // Only scan inside this test project directory (avoid picking up runtime api project files that still intentionally contain legacy code until Story 3/4)
        var offending = Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            // Exclude explicitly allow‑listed negative-path tests and this guard test itself
            .Where(f =>
            {
                var normalized = f.Replace("\\", "/");
                if (normalized.EndsWith("Guard/DevHeadersUsageGuardTests.cs")) return false; // ignore self so literals in comments don't trip
                return !AllowList.Any(a => normalized.EndsWith(a));
            })
            .Select(f => new { File = f, Text = File.ReadAllText(f) })
            .Where(x => x.Text.Contains("x-dev-user") || x.Text.Contains("x-tenant"))
            .Select(x => x.File)
            .ToList();

        Assert.True(offending.Count == 0, "Found unexpected dev header usage in test files:\n" + string.Join('\n', offending.Select(p => " - " + p)));
    }
}
