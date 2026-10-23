using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Services;

namespace LimitLens.Indexing.Claude;

/// <summary>Publishes the Claude limits that the status-line bridge records.</summary>
public sealed class ClaudeStatusLineClient(string usagePath, Func<DateTimeOffset>? clock = null) : IAccountUsageClient
{
    public const string BucketId = "claude";
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(150);
    // The file changes only when Claude Code renders; reloading also drops windows that have reset.
    private static readonly TimeSpan ExpiryCheckInterval = TimeSpan.FromMinutes(1);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? expiryLoop;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? pendingReload;
    private bool disposed;

    public AccountUsageSnapshot Current { get; private set; } = AccountUsageSnapshot.Empty;
    public SourceHealth Health { get; private set; } = SourceHealth.Starting("Looking for Claude Code limits…");

    public event Action<AccountUsageSnapshot>? SnapshotChanged;
    public event Action<SourceHealth>? HealthChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(usagePath)!;
        Directory.CreateDirectory(directory);
        lock (gate)
        {
            if (watcher is null && !disposed)
            {
                watcher = new FileSystemWatcher(directory, Path.GetFileName(usagePath))
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Changed += (_, _) => ScheduleReload();
                watcher.Created += (_, _) => ScheduleReload();
                watcher.Renamed += (_, _) => ScheduleReload();
                watcher.Deleted += (_, _) => ScheduleReload();
                watcher.EnableRaisingEvents = true;
                expiryLoop = ReloadPeriodicallyAsync(lifetime.Token);
            }
        }

        Reload();
        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Reload();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            watcher?.Dispose();
            watcher = null;
            pendingReload?.Cancel();
            pendingReload?.Dispose();
            pendingReload = null;
            lifetime.Cancel();
            loop = expiryLoop;
        }

        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }

        lifetime.Dispose();
    }

    public static RateLimitBucket? ToBucket(ClaudeUsageCapture capture, DateTimeOffset now)
    {
        var fiveHour = ToWindow(capture.FiveHour, AccountLimitSelector.FiveHourMinutes, now);
        var weekly = ToWindow(capture.SevenDay, AccountLimitSelector.WeeklyMinutes, now);
        return fiveHour is null && weekly is null
            ? null
            : new RateLimitBucket(
                BucketId,
                "Claude",
                Primary: fiveHour,
                Secondary: weekly,
                Provider: UsageProvider.Claude,
                UpdatedAt: capture.CapturedAt);
    }

    private static RateLimitWindow? ToWindow(ClaudeUsageWindow? window, long durationMinutes, DateTimeOffset now) =>
        // Claude Code drops a window once it resets; a stale reading is not carried into the next window.
        window is null || window.ResetsAt <= now
            ? null
            : new RateLimitWindow(
                (int)Math.Clamp(Math.Round(window.UsedPercentage, MidpointRounding.AwayFromZero), 0, 100),
                durationMinutes,
                window.ResetsAt);

    private async Task ReloadPeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ExpiryCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Reload();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleReload()
    {
        CancellationToken token;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pendingReload?.Cancel();
            pendingReload?.Dispose();
            pendingReload = new CancellationTokenSource();
            token = pendingReload.Token;
        }

        _ = Task.Delay(ReloadDelay, token).ContinueWith(
            _ => Reload(),
            token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void Reload()
    {
        var capture = ClaudeUsageFile.TryRead(usagePath);
        var bucket = capture is null ? null : ToBucket(capture, now());
        AccountUsageSnapshot snapshot;
        SourceHealth health;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            snapshot = AccountUsageSnapshot.Empty with
            {
                RateLimits = bucket is null ? [] : [bucket],
                UpdatedAt = capture?.CapturedAt ?? DateTimeOffset.MinValue,
            };
            health = capture is null
                ? new SourceHealth(
                    SourceConnectionState.Missing,
                    "Connect Claude Code in Settings to show Claude limits.",
                    Health.LastSuccessfulUpdate)
                : new SourceHealth(SourceConnectionState.Connected, "Reading Claude Code limits.", capture.CapturedAt);
            Current = snapshot;
            Health = health;
        }

        SnapshotChanged?.Invoke(snapshot);
        HealthChanged?.Invoke(health);
    }
}
