using FluentAssertions;
using Xunit;

namespace Appostolic.DevHeadersAnalyzer.Tests;

public class RDH0001NegativeTests
{
    private readonly DevHeaderArtifactsAnalyzer _analyzer = new();

    [Fact]
    public void NoForbiddenTokens_NoDiagnostics()
    {
        var src = "class C { void M() { var h = \"normal-value\"; } }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer);
        diags.Should().BeEmpty();
    }
}
