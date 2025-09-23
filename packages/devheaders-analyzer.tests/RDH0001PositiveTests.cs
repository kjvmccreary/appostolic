using FluentAssertions;
using Xunit;

namespace Appostolic.DevHeadersAnalyzer.Tests;

public class RDH0001PositiveTests
{
    private readonly DevHeaderArtifactsAnalyzer _analyzer = new();

    [Theory]
    [InlineData("x-dev-user")]
    [InlineData("x-tenant")]
    [InlineData("DevHeaderAuthHandler")]
    [InlineData("BearerOrDev")]
    [InlineData("AUTH__ALLOW_DEV_HEADERS")]
    public void StringLiteral_TriggersDiagnostic(string token)
    {
    var src = $"class C {{ void M() {{ var h = \"{token}\"; }} }}";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer);
        diags.Should().ContainSingle();
        diags[0].Id.Should().Be("RDH0001");
        diags[0].GetMessage().Should().Contain(token);
    }

    [Fact]
    public void Identifier_TriggersDiagnostic()
    {
        var src = "class BearerOrDev { }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer);
        diags.Should().ContainSingle();
        diags[0].GetMessage().Should().Contain("BearerOrDev");
    }

    [Fact]
    public void SubstringInsideLiteral_TriggersDiagnostic()
    {
        var src = "class C { void M() { var h = \"prefix-x-dev-user-suffix\"; } }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer);
        diags.Should().ContainSingle();
        diags[0].GetMessage().Should().Contain("x-dev-user");
    }

    [Fact]
    public void CaseInsensitiveMatch_TriggersDiagnostic()
    {
        var src = "class C { void M() { var h = \"X-DEV-USER\"; } }";
        var diags = AnalyzerTestHelper.GetDiagnostics(src, _analyzer);
        diags.Should().ContainSingle();
        diags[0].GetMessage().Should().Contain("x-dev-user");
    }
}
