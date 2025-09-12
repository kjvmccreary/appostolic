using Appostolic.Api.Application.Agents.Model;
using FluentAssertions;

namespace Appostolic.Api.Tests.Model;

public class TokenEstimatorTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("abcd", 1)]
    [InlineData("abcde", 2)]
    [InlineData("abcdefgh", 2)]
    [InlineData("abcdefghi", 3)]
    public void EstimateTokens_Heuristic_CeilPer4Chars(string text, int expected)
    {
        var tokens = TokenEstimator.EstimateTokens(text, model: "any");
        tokens.Should().Be(expected);
    }
}
