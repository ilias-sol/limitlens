using LimitLens.App.Services;
using LimitLens.App.ViewModels;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class DashboardViewModelTests
{
    [Fact]
    public void PersistsModeAndCardOrdering()
    {
        var settings = new DashboardSettings();
        var store = new FakeSettingsStore(settings);
        using var model = new DashboardViewModel(
            new FakeAccountClient(),
            new FakeIndexer(),
            store,
            new FakeStartupService(),
            settings,
            "Data");

        model.IsCompact = false;
        model.IsFlyoutPositionCenter = true;
        model.MoveCard(0, 2);
        var extraCompact = model.CardSettings.First(card => !card.IsCompact);
        extraCompact.IsCompact = true;

        Assert.True(model.IsExpanded);
        Assert.Equal(FlyoutPosition.Center, settings.FlyoutPosition);
        Assert.True(model.IsFlyoutPositionCenter);
        Assert.Equal(model.CardSettings.Select(card => card.Id), settings.CardOrder);
        Assert.False(extraCompact.IsCompact);
        Assert.True(store.SaveCount > 0);
    }

    [Fact]
    public void ForecastsDepletionFromTheCurrentResetWindowPace()
    {
        var now = DateTimeOffset.Now;
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", Primary: new RateLimitWindow(80, 300, now.AddHours(4)))],
            null,
            now);
        var reset = now.AddHours(4);
        var settings = new DashboardSettings
        {
            UsageHistory =
            [
                new UsageHistorySample { Timestamp = now.AddMinutes(-40), ResetAt = reset, RemainingPercent = 60 },
                new UsageHistorySample { Timestamp = now.AddMinutes(-20), ResetAt = reset, RemainingPercent = 40 },
                new UsageHistorySample { Timestamp = now, ResetAt = reset, RemainingPercent = 20 },
            ],
        };
        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            new FakeIndexer(),
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.Equal(20, model.ForecastRemainingPercent);
        Assert.Equal("ChatGPT Plus", model.PlanText);
        Assert.Contains(" · ", model.LimitRows.Single().PrimaryReset, StringComparison.Ordinal);
        Assert.True(model.ForecastHasPrediction);
        Assert.NotNull(model.ForecastEmptyAt);
        Assert.True(model.ForecastEmptyAt < model.ForecastResetAt);
        Assert.Contains("reaches 0%", model.ForecastSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void WaitsForEnoughRealHistoryBeforePredicting()
    {
        var now = DateTimeOffset.Now;
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", Primary: new RateLimitWindow(20, 300, now.AddHours(4)))],
            null,
            now);
        var settings = new DashboardSettings();

        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            new FakeIndexer(),
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.False(model.ForecastHasPrediction);
        Assert.Null(model.ForecastEmptyAt);
        Assert.Contains("Collecting usage history", model.ForecastSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportsTheCurrentWindowsLocalRateLimitCurveAndNormalizesResetDrift()
    {
        var now = DateTimeOffset.Now;
        var reset = now.AddDays(6);
        var start = reset - TimeSpan.FromDays(7);
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", Primary: new RateLimitWindow(40, 10_080, reset))],
            null,
            now);
        var indexer = new FakeIndexer
        {
            RateLimitHistory =
            [
                new UsageHistorySample { Timestamp = start, ResetAt = reset.AddSeconds(-6), RemainingPercent = 100 },
                new UsageHistorySample { Timestamp = start.AddHours(1), ResetAt = reset, RemainingPercent = 80 },
                new UsageHistorySample { Timestamp = start.AddHours(2), ResetAt = reset, RemainingPercent = 80 },
                new UsageHistorySample { Timestamp = start.AddHours(3), ResetAt = reset, RemainingPercent = 80 },
                new UsageHistorySample { Timestamp = start.AddHours(20), ResetAt = reset, RemainingPercent = 70 },
            ],
        };
        var settings = new DashboardSettings();

        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            indexer,
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.Equal([100, 80, 80, 70, 60], model.ForecastActualPoints.Select(sample => sample.RemainingPercent));
        Assert.All(model.ForecastActualPoints, sample => Assert.Equal(reset, sample.ResetAt));
        Assert.Equal(start, model.ForecastActualPoints[0].Timestamp);
        Assert.True(model.ForecastHasPrediction);
    }

    [Fact]
    public void CompactsRepeatedSnapshotsWithoutDiscardingTheWindowStart()
    {
        var now = DateTimeOffset.Now;
        var reset = now.AddHours(1);
        var start = reset - TimeSpan.FromDays(7);
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", Primary: new RateLimitWindow(40, 10_080, reset))],
            null,
            now);
        var settings = new DashboardSettings
        {
            UsageHistory = Enumerable.Range(0, 600)
                .Select(index => new UsageHistorySample
                {
                    Timestamp = start.AddMinutes(index * 10),
                    ResetAt = reset,
                    RemainingPercent = 80,
                })
                .ToList(),
        };

        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            new FakeIndexer(),
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.Equal([80, 80, 60], model.ForecastActualPoints.Select(sample => sample.RemainingPercent));
        Assert.Equal(start, model.ForecastActualPoints[0].Timestamp);
        Assert.Equal(now, model.ForecastActualPoints[^1].Timestamp);
    }

    [Fact]
    public void ShowcaseRepresentsFourDaysWithSixtySevenPercentRemaining()
    {
        var now = DateTimeOffset.Now;
        var client = new ShowcaseAccountClient(now);
        var settings = new DashboardSettings
        {
            UsageHistory = [.. client.UsageHistory],
            ShowCreditsInWidget = true,
        };

        using var model = new DashboardViewModel(
            client,
            new FakeIndexer(),
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.Equal(67, model.ForecastRemainingPercent);
        Assert.Equal(10, model.ForecastActualPoints.Count);
        Assert.True(model.ForecastHasPrediction);
        Assert.Equal("1484", model.CreditsText);
        Assert.Equal("2", model.ResetCreditsText);
    }

    [Theory]
    [InlineData("plus", "ChatGPT Plus")]
    [InlineData("pro", "ChatGPT Pro 5x")]
    [InlineData("pro-20x", "ChatGPT Pro 20x")]
    public void UsesOfficialCodexPlanLabels(string planType, string expected)
    {
        var snapshot = AccountUsageSnapshot.Empty with { PlanType = planType };
        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            new FakeIndexer(),
            new FakeSettingsStore(new DashboardSettings()),
            new FakeStartupService(),
            new DashboardSettings(),
            "Data");

        Assert.Equal(expected, model.PlanText);
    }

    [Fact]
    public void RecordsMeasuredUsageAndKeepsCreditsOptional()
    {
        var now = DateTimeOffset.Now;
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket(
                "codex",
                PlanType: "pro",
                Primary: new RateLimitWindow(35, 300, now.AddHours(4)),
                Credits: new CreditsSnapshot(true, false, "42.50831"))],
            new ResetCreditSummary(3),
            now);
        var settings = new DashboardSettings { ShowCreditsInWidget = false };
        using var model = new DashboardViewModel(
            new FakeAccountClient(snapshot),
            new FakeIndexer(),
            new FakeSettingsStore(settings),
            new FakeStartupService(),
            settings,
            "Data");

        Assert.Single(model.ForecastActualPoints);
        Assert.Equal(65, model.ForecastActualPoints[0].RemainingPercent);
        Assert.True(model.CreditsAvailable);
        Assert.False(model.CreditsVisible);
        Assert.Equal("ChatGPT Pro 5x", model.PlanText);
        Assert.Equal("3", model.ResetCreditsText);

        model.ShowCreditsInWidget = true;

        Assert.True(model.CreditsVisible);
        Assert.Equal("43", model.CreditsText);
    }

    private sealed class FakeAccountClient(AccountUsageSnapshot? snapshot = null) : ICodexAppServerClient
    {
        public AccountUsageSnapshot Current { get; } = snapshot ?? AccountUsageSnapshot.Empty;
        public SourceHealth Health { get; } = SourceHealth.Starting("test");
        public event Action<AccountUsageSnapshot>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise() => SnapshotChanged?.Invoke(Current);
        public void RaiseHealth() => HealthChanged?.Invoke(Health);
    }

    private sealed class FakeIndexer : ISessionLogIndexer
    {
        public LocalUsageAggregate Current { get; } = LocalUsageAggregate.Empty;
        public IReadOnlyList<UsageHistorySample> RateLimitHistory { get; init; } = [];
        public SourceHealth Health { get; } = SourceHealth.Starting("test");
        public event Action<LocalUsageAggregate>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public event Action<double>? BackfillProgressChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise() => SnapshotChanged?.Invoke(Current);
        public void RaiseHealth() => HealthChanged?.Invoke(Health);
        public void RaiseProgress() => BackfillProgressChanged?.Invoke(1);
    }

    private sealed class FakeSettingsStore(DashboardSettings current) : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public DashboardSettings Current { get; private set; } = current;
        public Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(DashboardSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStartupService : IStartupRegistrationService
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }
}
