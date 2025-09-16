using Appostolic.Api.Application.Privacy;
using Xunit;

namespace Appostolic.Api.Tests.Privacy;

public class PIIRedactorTests
{
    [Theory]
    [InlineData("user@example.com", "u***@example.com")]
    [InlineData("U@ex.com", "***U@ex.com")] // short local part fallback
    public void RedactEmail_Works(string input, string expected)
    {
        Assert.Equal(expected, PIIRedactor.RedactEmail(input));
    }

    [Theory]
    [InlineData("+1 (555) 123-4567", "***4567")]
    [InlineData("5551234567", "***4567")]
    [InlineData("1234", "***1234")] // short fallback: last 4 still entire input
    public void RedactPhone_Works(string input, string expected)
    {
        Assert.Equal(expected, PIIRedactor.RedactPhone(input));
    }
}
