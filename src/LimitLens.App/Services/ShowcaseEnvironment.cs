using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.App.Services;

internal sealed class ShowcaseSettingsStore(DashboardSettings settings) : ISettingsStore
{
    public DashboardSettings Current { get; private set; } = settings;

    public Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Current);
    }

    public Task SaveAsync(
        DashboardSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = settings;
        return Task.CompletedTask;
    }
}

internal sealed class ShowcaseSessionLogIndexer : ISessionLogIndexer
{
    public LocalUsageAggregate Current { get; } = LocalUsageAggregate.Empty;
    public IReadOnlyList<UsageHistorySample> RateLimitHistory { get; } = [];
    public SourceHealth Health { get; } = new(
        SourceConnectionState.LocalOnly,
        "Showcase mode does not read local Codex sessions.",
        DateTimeOffset.Now);

    public event Action<LocalUsageAggregate>? SnapshotChanged;
    public event Action<SourceHealth>? HealthChanged;
    public event Action<double>? BackfillProgressChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotChanged?.Invoke(Current);
        HealthChanged?.Invoke(Health);
        BackfillProgressChanged?.Invoke(1);
        return Task.CompletedTask;
    }

    public Task RebuildAsync(CancellationToken cancellationToken = default) =>
        StartAsync(cancellationToken);

    public Task DeleteAllAsync(CancellationToken cancellationToken = default) =>
        StartAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ShowcaseStartupRegistrationService : IStartupRegistrationService
{
    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
