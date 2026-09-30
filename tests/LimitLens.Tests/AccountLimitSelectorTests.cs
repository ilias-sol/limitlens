using LimitLens.Core.Models;
using LimitLens.Core.Services;

namespace LimitLens.Tests;

public sealed class AccountLimitSelectorTests
{
    [Theory]
    [InlineData("plus", true)]
    [InlineData("pro", false)]
    [InlineData("pro-20x", false)]
    [InlineData("business", false)]
    [InlineData("team", false)]
    [InlineData("enterprise", false)]
    [InlineData("edu", false)]
    public void SelectsCodexByIdAndWindowsByDuration(string plan, bool showBoth)
    {
        var now = DateTimeOffset.UtcNow;
        var shortWindow = new RateLimitWindow(85, 300, now.AddHours(2));
        var weekly = new RateLimitWindow(30, 10_080, now.AddDays(4));
        var snapshot = AccountUsageSnapshot.Empty with
        {
            PlanType = plan,
            RateLimits =
            [
                new("review", "A Reviews", "plus", new(0, 300, now.AddHours(2))),
                new("codex", "Codex", plan, weekly, shortWindow),
            ],
        };
        var selected = AccountLimitSelector.Select(snapshot);

        Assert.Equal("codex", selected.Bucket?.Id);
        Assert.Same(shortWindow, selected.FiveHour);
        Assert.Same(weekly, selected.Weekly);
        Assert.Same(weekly, selected.Forecast);
        Assert.Equal("Weekly", selected.ForecastLabel);
        Assert.Equal(showBoth, selected.ShowBoth);
    }

    [Fact]
    public void DoesNotRelabelAProFiveHourWindowAsWeekly()
    {
        var snapshot = AccountUsageSnapshot.Empty with
        {
            PlanType = "pro",
            RateLimits = [new("codex", Primary: new(20, 300, DateTimeOffset.Now.AddHours(2)))],
        };
        var selected = AccountLimitSelector.Select(snapshot);
        Assert.False(selected.ShowBoth);
        Assert.Null(selected.Weekly);
        Assert.Null(selected.Forecast);
    }

    [Fact]
    public void DoesNotUseReviewUsageWhenCodexIsUnavailable()
    {
        var selected = AccountLimitSelector.Select(AccountUsageSnapshot.Empty with
        {
            PlanType = "plus",
            RateLimits = [new("review", Primary: new(10, 10_080, DateTimeOffset.Now.AddDays(4)))],
        });
        Assert.Null(selected.Bucket);
        Assert.Null(selected.Forecast);
        Assert.True(selected.ShowBoth);
    }
}
