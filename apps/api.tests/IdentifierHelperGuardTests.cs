using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Appostolic.Api.Tests;

/// <summary>
/// Guard test ensuring deprecated ad-hoc unique identifier helper names are not reintroduced.
/// If failing, replace local helpers with `TestUtilities.UniqueId`.
/// </summary>
public class IdentifierHelperGuardTests
{
    [Fact]
    public void No_legacy_unique_identifier_helpers_present()
    {
        var root = Directory.GetCurrentDirectory(); // points to test assembly bin dir
        // Walk up to repo root by locating the solution marker file.
        var dir = new DirectoryInfo(root);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "appostolic.sln")))
            dir = dir.Parent!;

        dir.Should().NotBeNull("we should locate repo root for scanning");

        var testDir = Path.Combine(dir!.FullName, "apps", "api.tests");
        var forbidden = new[] { "UniqueFrag(", "UniqueSlug(", "UniqueEmail(" };
        var offenders = Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("IdentifierHelperGuardTests.cs"))
            .SelectMany(p => File.ReadLines(p).Select((line, idx) => (p, line, idx)))
            .Where(t => forbidden.Any(f => t.line.Contains(f)))
            .Select(t => $"{t.p}:{t.idx + 1}: {t.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("legacy unique identifier helpers must not be reintroduced; use UniqueId instead\n" + string.Join('\n', offenders));
    }
}
