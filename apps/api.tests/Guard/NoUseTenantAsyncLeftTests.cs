using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Appostolic.Api.Tests.Guard;

/// <summary>
/// Provisional guard test for RDH Story 2: enumerates any remaining usages of AuthTestClient.UseTenantAsync.
/// Currently WARN-ONLY (does not fail) unless the environment variable AUTH_GUARD_ENFORCE_USE_TENANT_ASYNC == "1".
/// Once all migrations complete, set the env var in CI to convert warnings into a failing assertion
/// and then remove allowlisted files. Allowlist lets us keep intentional coverage tests until removal.
/// </summary>
public class NoUseTenantAsyncLeftTests
{
    private static readonly string[] AllowList = new[]
    {
        // Temporary: helper tests or intentional legacy references (trim as migrations finish)
        "AuthTestClientTests.cs" // tests explicitly validating the old helper (will be removed later)
    };

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

        var enforce = Environment.GetEnvironmentVariable("AUTH_GUARD_ENFORCE_USE_TENANT_ASYNC") == "1";
        if (matches.Count > 0)
        {
            var msg = $"Found {matches.Count} remaining AuthTestClient.UseTenantAsync usages:\n" + string.Join('\n', matches.Select(Path.GetFileName));
            if (enforce)
            {
                Assert.True(matches.Count == 0, msg); // fail build
            }
            else
            {
                // Non-enforcing: write to console so it appears in test logs.
                Console.WriteLine("[guard][UseTenantAsync][warning] " + msg);
            }
        }
        else
        {
            Console.WriteLine("[guard][UseTenantAsync] No remaining usages detected (allowlist ignored).");
        }
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
