using LimitLens.Core.Models;

namespace LimitLens.Tests;

public sealed class TokenUsageBreakdownTests
{
    [Fact]
    public void PositiveDeltaSubtractsCumulativeCounters()
    {
        var previous = new TokenUsageBreakdown(100, 30, 5, 20, 4, 120);
        var current = new TokenUsageBreakdown(145, 42, 6, 31, 8, 176);

        var delta = current.PositiveDeltaFrom(previous);

        Assert.Equal(new TokenUsageBreakdown(45, 12, 1, 11, 4, 56), delta);
    }

    [Fact]
    public void PositiveDeltaTreatsCounterRegressionAsNewEpoch()
    {
        var previous = new TokenUsageBreakdown(900, 500, 20, 200, 50, 1_100);
        var reset = new TokenUsageBreakdown(40, 10, 0, 8, 2, 48);

        Assert.Equal(reset, reset.PositiveDeltaFrom(previous));
    }
}
