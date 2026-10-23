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
    public void ClaudeAlwaysPairsItsFiveHourAndWeeklyWindows()
    {
        var now = DateTimeOffset.Now;
        var snapshot = AccountUsageSnapshot.Empty with
        {
            PlanType = "pro",
            RateLimits =
            [
                new("claude", Primary: new(20, 300, now.AddHours(1)), Secondary: new(50, 10_080, now.AddDays(2)),
                    Provider: UsageProvider.Claude),
            ],
        };

        var claude = AccountLimitSelector.Select(snapshot, UsageProvider.Claude);
        Assert.True(claude.ShowBoth);
        Assert.Equal(50, claude.Weekly?.UsedPercent);
        // The Codex plan rules must not apply to Claude, and a lone Claude bucket is not a Codex bucket.
        Assert.Null(AccountLimitSelector.Select(snapshot).Bucket);
        Assert.Equal(UsageProvider.Claude, AccountLimitSelector.AutomaticProvider(snapshot));

        var (primary, secondary) = AccountLimitSelector.ResolveTaskbar(snapshot, LimitKeys.Auto, LimitKeys.Auto);
        Assert.Equal((UsageProvider.Claude, "5-hour", 20), (primary.Provider, primary.WindowLabel, primary.Window?.UsedPercent));
        Assert.Equal((UsageProvider.Claude, "Weekly", 50), (secondary?.Provider, secondary?.WindowLabel, secondary?.Window?.UsedPercent));
    }

    [Fact]
    public void ResolvesChosenLimitsAcrossProviders()
    {
        var now = DateTimeOffset.Now;
        var snapshot = AccountUsageSnapshot.Empty with
        {
            PlanType = "pro",
            RateLimits =
            [
                new("codex", Primary: new(30, 10_080, now.AddDays(3)), Secondary: new(5, 300, now.AddHours(2))),
                new("claude", Primary: new(20, 300, now.AddHours(1)), Secondary: new(50, 10_080, now.AddDays(2)),
                    Provider: UsageProvider.Claude),
            ],
        };

        var (primary, secondary) = AccountLimitSelector.ResolveTaskbar(snapshot, LimitKeys.ClaudeFiveHour, LimitKeys.CodexWeekly);
        Assert.Equal(20, primary.Window?.UsedPercent);
        Assert.Equal(30, secondary?.Window?.UsedPercent);

        // Automatic keeps today's Codex Pro layout: one weekly bar.
        var automatic = AccountLimitSelector.ResolveTaskbar(snapshot, LimitKeys.Auto, LimitKeys.Auto);
        Assert.Equal((UsageProvider.Codex, "Weekly"), (automatic.Primary.Provider, automatic.Primary.WindowLabel));
        Assert.Null(automatic.Secondary);
        // A chosen five-hour Codex window is shown even though Pro's layout hides it.
        Assert.Equal(5, AccountLimitSelector.Resolve(snapshot, LimitKeys.CodexFiveHour).Window?.UsedPercent);
        Assert.Null(AccountLimitSelector.ResolveTaskbar(snapshot, LimitKeys.Auto, LimitKeys.None).Secondary);

        Assert.Null(AccountLimitSelector.ResolveForecast(snapshot, LimitKeys.None));
        Assert.Equal(50, AccountLimitSelector.ResolveForecast(snapshot, LimitKeys.ClaudeWeekly)?.Window?.UsedPercent);
        Assert.Equal(30, AccountLimitSelector.ResolveForecast(snapshot, LimitKeys.Auto)?.Window?.UsedPercent);
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
