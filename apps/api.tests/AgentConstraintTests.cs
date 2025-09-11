using Appostolic.Api.Domain.Agents;

namespace Appostolic.Api.Tests;

public class AgentConstraintTests
{
    // Domain-level invariant tests only; no DbContext needed here

    [Fact]
    public void Agent_MaxSteps_out_of_range_throws_in_domain_constructor()
    {
        Action actLow = () => new Agent(Guid.NewGuid(), "Name", "", Array.Empty<string>(), "model", 0.2, 0);
        Action actHigh = () => new Agent(Guid.NewGuid(), "Name", "", Array.Empty<string>(), "model", 0.2, 100);
        actLow.Should().Throw<ArgumentOutOfRangeException>();
        actHigh.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Agent_Temperature_out_of_range_throws_in_domain_constructor()
    {
        Action actLow = () => new Agent(Guid.NewGuid(), "Name", "", Array.Empty<string>(), "model", -0.1, 8);
        Action actHigh = () => new Agent(Guid.NewGuid(), "Name", "", Array.Empty<string>(), "model", 2.1, 8);
        actLow.Should().Throw<ArgumentOutOfRangeException>();
        actHigh.Should().Throw<ArgumentOutOfRangeException>();
    }
}
