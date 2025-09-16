using Appostolic.Api.Application.Privacy;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Privacy;

public class PIIHasherTests
{
    private static Sha256PIIHasher Create(string pepper = "pepper1") => new(Options.Create(new PrivacyOptions
    {
        PIIHashPepper = pepper,
        PIIHashingEnabled = true
    }));

    [Fact]
    public void HashEmail_IsDeterministic_ForSamePepper()
    {
        var hasher = Create();
        var a = hasher.HashEmail("User@Example.COM ");
        var b = hasher.HashEmail("user@example.com");
        Assert.Equal(a, b); // normalization
    }

    [Fact]
    public void HashEmail_Differs_WithDifferentPepper()
    {
        var h1 = Create("p1").HashEmail("user@example.com");
        var h2 = Create("p2").HashEmail("user@example.com");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashPhone_NormalizesDigits()
    {
        var hasher = Create();
        var a = hasher.HashPhone("+1 (555) 123-4567");
        var b = hasher.HashPhone("15551234567");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashPhone_Differs_WithDifferentPepper()
    {
        var h1 = Create("ppA").HashPhone("+1 555 123 4567");
        var h2 = Create("ppB").HashPhone("+1 555 123 4567");
        Assert.NotEqual(h1, h2);
    }
}
