using System;
using Appostolic.Api.Infrastructure.Auth.Refresh;
using Microsoft.Extensions.Options;
using Xunit;

namespace Appostolic.Api.Tests.Auth;

/// <summary>
/// Unit tests for <see cref="InMemoryRefreshRateLimiter"/> covering boundary, exceed, window reset, and dry-run semantics.
/// </summary>
public class RefreshRateLimiterTests
{
    private static InMemoryRefreshRateLimiter Create(int windowSeconds = 60, int max = 3, bool dryRun = false)
    {
        return new InMemoryRefreshRateLimiter(Options.Create(new RefreshRateLimitOptions
        {
            WindowSeconds = windowSeconds,
            Max = max,
            DryRun = dryRun
        }));
    }

    [Fact]
    public void UnderThreshold_Allows()
    {
        var rl = Create(max: 3);
        var ip = "1.1.1.1";
        for (int i = 0; i < 3; i++)
        {
            var eval = rl.Evaluate(null, ip);
            Assert.False(eval.IsLimited);
            Assert.Equal(i + 1, eval.Attempts);
        }
    }

    [Fact]
    public void ExceedThreshold_Blocks()
    {
        var rl = Create(max: 2);
        var ip = "2.2.2.2";
        rl.Evaluate(null, ip); // attempt 1
        rl.Evaluate(null, ip); // attempt 2 (boundary)
        var blocked = rl.Evaluate(null, ip); // attempt 3
        Assert.True(blocked.IsLimited);
        Assert.Equal(3, blocked.Attempts);
        Assert.Equal(0, blocked.Remaining);
    }

    [Fact]
    public void BoundaryEqualsMax_NotBlocked()
    {
        var rl = Create(max: 2);
        var ip = "3.3.3.3";
        var a1 = rl.Evaluate(null, ip);
        var a2 = rl.Evaluate(null, ip); // boundary (== max)
        Assert.False(a2.IsLimited);
        Assert.Equal(2, a2.Attempts);
    }

    [Fact]
    public void DryRun_NeverBlocks()
    {
        var rl = Create(max: 1, dryRun: true);
        var ip = "4.4.4.4";
        for (int i = 0; i < 5; i++)
        {
            var eval = rl.Evaluate(null, ip);
            Assert.False(eval.IsLimited);
        }
    }

    [Fact]
    public void UserDimension_IsolatedFromIpOnly()
    {
        var rl = Create(max: 2);
        var ip = "5.5.5.5";
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        rl.Evaluate(userA, ip); // A1
        rl.Evaluate(userA, ip); // A2 boundary
        var aBlocked = rl.Evaluate(userA, ip); // A3 blocked
        Assert.True(aBlocked.IsLimited);
        // User B should still have clean slate
        var b1 = rl.Evaluate(userB, ip);
        Assert.False(b1.IsLimited);
        Assert.Equal(1, b1.Attempts);
    }

    [Fact]
    public void WindowReset_AllowsAfterExpiry()
    {
        // We simulate by creating a very small window and sleeping just over it.
        var rl = Create(windowSeconds: 1, max: 1);
        var ip = "6.6.6.6";
        var first = rl.Evaluate(null, ip);
        Assert.False(first.IsLimited);
        System.Threading.Thread.Sleep(1100); // >1s
        var second = rl.Evaluate(null, ip);
        Assert.False(second.IsLimited);
        Assert.Equal(1, second.Attempts); // window should have reset
    }
}
