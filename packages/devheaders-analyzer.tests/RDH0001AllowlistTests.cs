using FluentAssertions;
using Xunit;

namespace Appostolic.DevHeadersAnalyzer.Tests;

public class RDH0001AllowlistTests
{
    private readonly DevHeaderArtifactsAnalyzer _analyzer = new();

    [Fact]
    public void StoryLogPath_IsIgnored()
    {
        var src = "class C { void M() { var h = \"x-dev-user\"; } }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer, "/repo/devInfo/storyLog/entry.cs");
        diags.Should().BeEmpty();
    }

    [Fact]
    public void DevHeadersRemovedTests_IsIgnored()
    {
        var src = "class C { void M() { var h = \"BearerOrDev\"; } }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer, "/repo/apps/api.tests/DevHeadersRemovedTests.cs");
        diags.Should().BeEmpty();
    }
}
