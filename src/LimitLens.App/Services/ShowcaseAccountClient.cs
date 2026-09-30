using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.App.Services;

public sealed class ShowcaseAccountClient : ICodexAppServerClient
{
    public ShowcaseAccountClient(DateTimeOffset now)
    {
        var start = now.AddDays(-4);
        var reset = start.AddDays(7);
        var points = new (double Days, int Remaining)[]
        {
            (0, 100), (0.45, 98), (0.9, 95), (1.35, 92), (1.8, 88),
            (2.25, 85), (2.7, 81), (3.15, 76), (3.55, 72), (4, 67),
        };

        UsageHistory = points.Select(point => new UsageHistorySample
        {
            Timestamp = start.AddDays(point.Days),
            RemainingPercent = point.Remaining,
            ResetAt = reset,
            WindowDurationMinutes = 7 * 24 * 60,
            LimitId = "codex",
        }).ToArray();
        Current = new AccountUsageSnapshot(
            "plus",
            new AccountUsageSummary(),
            [],
            [new RateLimitBucket(
                "codex",
                "Codex",
                "plus",
                new RateLimitWindow(24, 300, now.AddHours(3)),
                new RateLimitWindow(33, 7 * 24 * 60, reset),
                Credits: new CreditsSnapshot(true, false, "1484.37"))],
            new ResetCreditSummary(2),
            now);
        Health = new SourceHealth(SourceConnectionState.Connected, "Showcase data", now);
    }

    public IReadOnlyList<UsageHistorySample> UsageHistory { get; }
    public AccountUsageSnapshot Current { get; private set; }
    public SourceHealth Health { get; private set; }
    public event Action<AccountUsageSnapshot>? SnapshotChanged;
    public event Action<SourceHealth>? HealthChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        SnapshotChanged?.Invoke(Current);
        HealthChanged?.Invoke(Health);
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        Current = Current with { UpdatedAt = now };
        Health = Health with { LastSuccessfulUpdate = now };
        SnapshotChanged?.Invoke(Current);
        HealthChanged?.Invoke(Health);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
