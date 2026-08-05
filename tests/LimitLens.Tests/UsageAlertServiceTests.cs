using LimitLens.App.Services;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class UsageAlertServiceTests
{
    [Fact]
    public async Task DeduplicatesEachThresholdByLimitWindowAndReset()
    {
        var settings = new DashboardSettings { AlertThresholds = [25, 10, 0] };
        var account = new FakeAccountClient();
        var sink = new FakeNotificationSink();
        using var service = new UsageAlertService(account, new FakeSettingsStore(settings), settings, sink);
        var reset = DateTimeOffset.UtcNow.AddHours(2);
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", "Codex", "plus", new RateLimitWindow(91, 300, reset))],
            null,
            DateTimeOffset.UtcNow);

        await service.EvaluateAsync(snapshot);
        await service.EvaluateAsync(snapshot);

        Assert.Single(sink.Messages);
        Assert.Equal(2, settings.AlertDedupeKeys.Count);
        Assert.Contains("10% remaining", sink.Messages[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RespectsMuteUntilReset()
    {
        var settings = new DashboardSettings
        {
            AlertThresholds = [25],
            AlertsMutedUntil = DateTimeOffset.UtcNow.AddHours(1),
        };
        var sink = new FakeNotificationSink();
        using var service = new UsageAlertService(new FakeAccountClient(), new FakeSettingsStore(settings), settings, sink);
        var snapshot = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket("codex", Primary: new RateLimitWindow(90, 300, DateTimeOffset.UtcNow.AddHours(1)))],
            null,
            DateTimeOffset.UtcNow);

        await service.EvaluateAsync(snapshot);

        Assert.Empty(sink.Messages);
    }

    private sealed class FakeNotificationSink : IAppNotificationSink
    {
        public List<(string Title, string Message)> Messages { get; } = [];
        public void Show(string title, string message) => Messages.Add((title, message));
    }

    private sealed class FakeSettingsStore(DashboardSettings settings) : ISettingsStore
    {
        public DashboardSettings Current { get; private set; } = settings;
        public Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(DashboardSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccountClient : ICodexAppServerClient
    {
        public AccountUsageSnapshot Current { get; } = AccountUsageSnapshot.Empty;
        public SourceHealth Health { get; } = SourceHealth.Starting("test");
        public event Action<AccountUsageSnapshot>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise(AccountUsageSnapshot snapshot) => SnapshotChanged?.Invoke(snapshot);
        public void RaiseHealth() => HealthChanged?.Invoke(Health);
    }
}
