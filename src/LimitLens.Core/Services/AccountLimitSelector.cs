using LimitLens.Core.Models;

namespace LimitLens.Core.Services;

public sealed record SelectedAccountLimits(
    RateLimitBucket? Bucket,
    RateLimitWindow? FiveHour,
    RateLimitWindow? Weekly,
    bool ShowBoth,
    RateLimitWindow? Forecast,
    string ForecastLabel);

/// <summary>A limit chosen for display. <see cref="Window"/> is null when its data is unavailable.</summary>
public sealed record DisplayedLimit(
    UsageProvider Provider,
    RateLimitBucket? Bucket,
    RateLimitWindow? Window,
    string WindowLabel);

public static class LimitKeys
{
    public const string Auto = "auto";
    public const string None = "none";
    public const string CodexFiveHour = "codex:5h";
    public const string CodexWeekly = "codex:weekly";
    public const string ClaudeFiveHour = "claude:5h";
    public const string ClaudeWeekly = "claude:weekly";

    public static IReadOnlyList<string> Windows { get; } =
        [CodexFiveHour, CodexWeekly, ClaudeFiveHour, ClaudeWeekly];

    public static bool IsWindow(string? key) => key is not null && Windows.Contains(key, StringComparer.Ordinal);

    public static string Normalize(string? key, bool allowNone) =>
        IsWindow(key) || key == Auto || allowNone && key == None ? key! : Auto;

    public static UsageProvider ProviderOf(string key) =>
        key.StartsWith("claude:", StringComparison.Ordinal) ? UsageProvider.Claude : UsageProvider.Codex;

    public static bool IsWeekly(string key) => key.EndsWith(":weekly", StringComparison.Ordinal);
}

public static class AccountLimitSelector
{
    public const long FiveHourMinutes = 300;
    public const long WeeklyMinutes = 7 * 24 * 60;

    public static SelectedAccountLimits Select(AccountUsageSnapshot snapshot) =>
        Select(snapshot, UsageProvider.Codex);

    public static SelectedAccountLimits Select(AccountUsageSnapshot snapshot, UsageProvider provider) =>
        provider == UsageProvider.Claude ? SelectClaude(snapshot) : SelectCodex(snapshot);

    public static bool HasData(AccountUsageSnapshot snapshot, UsageProvider provider) =>
        Select(snapshot, provider).Bucket is { } bucket && (bucket.Primary is not null || bucket.Secondary is not null);

    /// <summary>The provider "auto" settings follow: Codex when it has data, otherwise Claude.</summary>
    public static UsageProvider AutomaticProvider(AccountUsageSnapshot snapshot) =>
        !HasData(snapshot, UsageProvider.Codex) && HasData(snapshot, UsageProvider.Claude)
            ? UsageProvider.Claude
            : UsageProvider.Codex;

    public static DisplayedLimit Resolve(AccountUsageSnapshot snapshot, string key)
    {
        var provider = LimitKeys.ProviderOf(key);
        var selected = Select(snapshot, provider);
        return LimitKeys.IsWeekly(key)
            ? new DisplayedLimit(provider, selected.Bucket, selected.Weekly, "Weekly")
            : new DisplayedLimit(provider, selected.Bucket, selected.FiveHour, "5-hour");
    }

    public static (DisplayedLimit Primary, DisplayedLimit? Secondary) ResolveTaskbar(
        AccountUsageSnapshot snapshot,
        string primaryKey,
        string secondaryKey)
    {
        DisplayedLimit primary;
        SelectedAccountLimits primarySelection;
        if (LimitKeys.IsWindow(primaryKey))
        {
            primary = Resolve(snapshot, primaryKey);
            primarySelection = Select(snapshot, primary.Provider);
        }
        else
        {
            var provider = AutomaticProvider(snapshot);
            primarySelection = Select(snapshot, provider);
            primary = primarySelection.ShowBoth
                ? new DisplayedLimit(provider, primarySelection.Bucket, primarySelection.FiveHour, "5-hour")
                : new DisplayedLimit(
                    provider,
                    primarySelection.Bucket,
                    primarySelection.Forecast,
                    primarySelection.ForecastLabel == "Weekly" ? "Weekly" : "Usage");
        }

        DisplayedLimit? secondary = secondaryKey switch
        {
            LimitKeys.None => null,
            _ when LimitKeys.IsWindow(secondaryKey) => Resolve(snapshot, secondaryKey),
            // Automatic pairs a five-hour bar with its own provider's weekly bar, as Plus does today.
            _ when primarySelection.ShowBoth && primary.WindowLabel == "5-hour" =>
                new DisplayedLimit(primary.Provider, primarySelection.Bucket, primarySelection.Weekly, "Weekly"),
            _ => null,
        };
        return (primary, secondary);
    }

    public static DisplayedLimit? ResolveForecast(AccountUsageSnapshot snapshot, string key)
    {
        if (key == LimitKeys.None)
        {
            return null;
        }

        if (LimitKeys.IsWindow(key))
        {
            return Resolve(snapshot, key);
        }

        var provider = AutomaticProvider(snapshot);
        var selected = Select(snapshot, provider);
        return new DisplayedLimit(provider, selected.Bucket, selected.Forecast, selected.ForecastLabel);
    }

    private static SelectedAccountLimits SelectCodex(AccountUsageSnapshot snapshot)
    {
        var codexBuckets = snapshot.RateLimits.Where(candidate => candidate.Provider == UsageProvider.Codex).ToArray();
        var bucket = codexBuckets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, "codex", StringComparison.OrdinalIgnoreCase));
        // Older servers can expose a single, unnamed Codex bucket. Never substitute a review bucket.
        bucket ??= codexBuckets.Length == 1 &&
                   !codexBuckets[0].Id.Contains("review", StringComparison.OrdinalIgnoreCase)
            ? codexBuckets[0]
            : null;
        var windows = new[] { bucket?.Primary, bucket?.Secondary };
        var shortWindow = windows.FirstOrDefault(window => window?.WindowDurationMinutes == FiveHourMinutes);
        var weekly = windows.FirstOrDefault(window => window?.WindowDurationMinutes == WeeklyMinutes);
        var plan = (bucket?.PlanType ?? snapshot.PlanType)?.Trim().ToLowerInvariant();
        var weeklyOnly = plan is "business" or "team" or "enterprise" or "edu" ||
                         plan?.StartsWith("pro", StringComparison.Ordinal) == true;
        var showBoth = !weeklyOnly && (plan == "plus" || shortWindow is not null);
        var forecast = weekly ?? (weeklyOnly ? null : shortWindow);
        // Preserve other server-defined windows without relabelling them as a week or five hours.
        forecast ??= weeklyOnly ? null : windows.FirstOrDefault(window => window is not null);
        return new SelectedAccountLimits(bucket, shortWindow, weekly, showBoth, forecast, Label(forecast, weeklyOnly));
    }

    private static SelectedAccountLimits SelectClaude(AccountUsageSnapshot snapshot)
    {
        // Claude plans (Pro and Max alike) expose a five-hour session window beside the weekly one.
        var bucket = snapshot.RateLimits.FirstOrDefault(candidate => candidate.Provider == UsageProvider.Claude);
        var windows = new[] { bucket?.Primary, bucket?.Secondary };
        var shortWindow = windows.FirstOrDefault(window => window?.WindowDurationMinutes == FiveHourMinutes);
        var weekly = windows.FirstOrDefault(window => window?.WindowDurationMinutes == WeeklyMinutes);
        var forecast = weekly ?? shortWindow;
        return new SelectedAccountLimits(bucket, shortWindow, weekly, shortWindow is not null, forecast, Label(forecast, false));
    }

    private static string Label(RateLimitWindow? window, bool weeklyOnly) => window?.WindowDurationMinutes switch
    {
        WeeklyMinutes => "Weekly",
        FiveHourMinutes => "5-hour",
        _ when weeklyOnly => "Weekly",
        _ => "Usage",
    };
}
