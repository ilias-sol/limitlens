using LimitLens.Core.Models;

namespace LimitLens.Core.Services;

public sealed record SelectedAccountLimits(
    RateLimitBucket? Bucket,
    RateLimitWindow? FiveHour,
    RateLimitWindow? Weekly,
    bool ShowBoth,
    RateLimitWindow? Forecast,
    string ForecastLabel);

public static class AccountLimitSelector
{
    public const long FiveHourMinutes = 300;
    public const long WeeklyMinutes = 7 * 24 * 60;

    public static SelectedAccountLimits Select(AccountUsageSnapshot snapshot)
    {
        var bucket = snapshot.RateLimits.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, "codex", StringComparison.OrdinalIgnoreCase));
        // Older servers can expose a single, unnamed Codex bucket. Never substitute a review bucket.
        bucket ??= snapshot.RateLimits.Count == 1 &&
                   !snapshot.RateLimits[0].Id.Contains("review", StringComparison.OrdinalIgnoreCase)
            ? snapshot.RateLimits[0]
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
        var label = forecast?.WindowDurationMinutes switch
        {
            WeeklyMinutes => "Weekly",
            FiveHourMinutes => "5-hour",
            _ when weeklyOnly => "Weekly",
            _ => "Usage",
        };
        return new SelectedAccountLimits(bucket, shortWindow, weekly, showBoth, forecast, label);
    }
}
