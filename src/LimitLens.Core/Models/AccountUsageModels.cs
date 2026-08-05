using System.Text.Json;

namespace LimitLens.Core.Models;

public sealed record AccountUsageSummary(
    long? LifetimeTokens = null,
    long? PeakDailyTokens = null,
    long? CurrentStreakDays = null,
    long? LongestStreakDays = null,
    long? LongestRunningTurnSeconds = null);

public sealed record DailyTokenUsage(DateOnly Date, long Tokens);

public sealed record RateLimitWindow(
    int UsedPercent,
    long? WindowDurationMinutes = null,
    DateTimeOffset? ResetsAt = null)
{
    public int RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

public sealed record CreditsSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance = null);

public sealed record SpendControlSnapshot(
    string Limit,
    string Used,
    int RemainingPercent,
    DateTimeOffset ResetsAt);

public sealed record RateLimitBucket(
    string Id,
    string? Name = null,
    string? PlanType = null,
    RateLimitWindow? Primary = null,
    RateLimitWindow? Secondary = null,
    CreditsSnapshot? Credits = null,
    SpendControlSnapshot? IndividualLimit = null,
    bool? SpendControlReached = null,
    string? RateLimitReachedType = null,
    IReadOnlyDictionary<string, JsonElement>? Extensions = null)
{
    public RateLimitBucket MergeSparse(RateLimitBucket update) => this with
    {
        Name = update.Name ?? Name,
        PlanType = update.PlanType ?? PlanType,
        Primary = update.Primary ?? Primary,
        Secondary = update.Secondary ?? Secondary,
        Credits = update.Credits ?? Credits,
        IndividualLimit = update.IndividualLimit ?? IndividualLimit,
        SpendControlReached = update.SpendControlReached ?? SpendControlReached,
        RateLimitReachedType = update.RateLimitReachedType ?? RateLimitReachedType,
        Extensions = MergeExtensions(Extensions, update.Extensions),
    };

    private static IReadOnlyDictionary<string, JsonElement>? MergeExtensions(
        IReadOnlyDictionary<string, JsonElement>? current,
        IReadOnlyDictionary<string, JsonElement>? update)
    {
        if (current is null)
        {
            return update;
        }

        if (update is null)
        {
            return current;
        }

        var merged = new Dictionary<string, JsonElement>(current, StringComparer.Ordinal);
        foreach (var (key, value) in update)
        {
            merged[key] = value;
        }

        return merged;
    }
}

public sealed record ResetCreditSummary(int AvailableCount = 0);

public sealed record AccountUsageSnapshot(
    string? PlanType,
    AccountUsageSummary Summary,
    IReadOnlyList<DailyTokenUsage> DailyUsage,
    IReadOnlyList<RateLimitBucket> RateLimits,
    ResetCreditSummary? ResetCredits,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, JsonElement>? Extensions = null)
{
    public static AccountUsageSnapshot Empty { get; } = new(
        null,
        new AccountUsageSummary(),
        [],
        [],
        null,
        DateTimeOffset.MinValue);
}
