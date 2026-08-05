using System.Globalization;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.App.Services;

public sealed class UsageAlertService : IDisposable
{
    private readonly ICodexAppServerClient accountClient;
    private readonly ISettingsStore settingsStore;
    private readonly DashboardSettings settings;
    private readonly IAppNotificationSink notifications;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public UsageAlertService(
        ICodexAppServerClient accountClient,
        ISettingsStore settingsStore,
        DashboardSettings settings,
        IAppNotificationSink notifications)
    {
        this.accountClient = accountClient;
        this.settingsStore = settingsStore;
        this.settings = settings;
        this.notifications = notifications;
        accountClient.SnapshotChanged += OnSnapshotChanged;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        accountClient.SnapshotChanged -= OnSnapshotChanged;
        gate.Dispose();
    }

    private void OnSnapshotChanged(AccountUsageSnapshot snapshot) => _ = EvaluateAsync(snapshot);

    public async Task EvaluateAsync(AccountUsageSnapshot snapshot)
    {
        if (!settings.AlertsEnabled || settings.AlertsMutedUntil > DateTimeOffset.Now)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var keys = settings.AlertDedupeKeys.ToHashSet(StringComparer.Ordinal);
            var changed = false;
            foreach (var bucket in snapshot.RateLimits)
            {
                changed |= EvaluateWindow(bucket, "primary", bucket.Primary, keys);
                changed |= EvaluateWindow(bucket, "secondary", bucket.Secondary, keys);
            }

            if (changed)
            {
                settings.AlertDedupeKeys = keys.TakeLast(256).ToList();
                await settingsStore.SaveAsync(settings).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private bool EvaluateWindow(
        RateLimitBucket bucket,
        string windowName,
        RateLimitWindow? window,
        ISet<string> keys)
    {
        if (window is null)
        {
            return false;
        }

        var changed = false;
        var resetKey = window.ResetsAt?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var remaining = window.RemainingPercent;
        var crossed = settings.AlertThresholds
            .Where(threshold => remaining <= threshold)
            .Order()
            .ToArray();
        if (crossed.Length == 0)
        {
            return false;
        }

        var notificationThreshold = crossed[0];
        var shouldNotify = false;
        foreach (var threshold in crossed)
        {
            var key = $"{bucket.Id}|{windowName}|{resetKey}|{threshold}";
            var added = keys.Add(key);
            changed |= added;
            shouldNotify |= added && threshold == notificationThreshold;
        }

        if (shouldNotify)
        {
            var resetText = window.ResetsAt is { } reset
                ? $" It resets {reset.LocalDateTime:g}."
                : string.Empty;
            notifications.Show(
                $"Limit Lens: {notificationThreshold}% remaining",
                $"{bucket.Name ?? bucket.Id} has {remaining}% remaining.{resetText}");
        }

        return changed;
    }
}
