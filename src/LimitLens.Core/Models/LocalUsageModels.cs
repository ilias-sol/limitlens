namespace LimitLens.Core.Models;

public enum TaskOutcome
{
    InProgress,
    Completed,
    Aborted,
    Failed,
}
public enum ToolCategory
{
    Shell,
    Patch,
    Web,
    Browser,
    Connector,
    Image,
    Other,
}

public sealed record DailyLocalUsage(
    DateOnly Date,
    TokenUsageBreakdown Tokens,
    int Tasks,
    int CompletedTasks,
    long DurationMilliseconds);

public sealed record ProjectUsage(
    string ProjectId,
    string DisplayName,
    TokenUsageBreakdown Tokens,
    int Tasks,
    long DurationMilliseconds);

public sealed record ModelUsage(
    string Model,
    TokenUsageBreakdown Tokens,
    int Tasks);

public sealed record ToolCategoryUsage(ToolCategory Category, int Count);

public sealed record CurrentTaskSnapshot(
    string SessionId,
    string TurnId,
    string ProjectDisplayName,
    string? Model,
    TokenUsageBreakdown Tokens,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed,
    TaskOutcome Outcome)
{
    public static CurrentTaskSnapshot? None => null;
}

public sealed record LocalUsageAggregate(
    TokenUsageBreakdown Tokens,
    int Sessions,
    int Tasks,
    int CompletedTasks,
    int AbortedTasks,
    long TotalDurationMilliseconds,
    long AverageTimeToFirstTokenMilliseconds,
    IReadOnlyList<DailyLocalUsage> DailyUsage,
    IReadOnlyList<ProjectUsage> Projects,
    IReadOnlyList<ModelUsage> Models,
    IReadOnlyList<ToolCategoryUsage> ToolCategories,
    CurrentTaskSnapshot? CurrentTask,
    DateTimeOffset UpdatedAt)
{
    public static LocalUsageAggregate Empty { get; } = new(
        TokenUsageBreakdown.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        [],
        [],
        [],
        [],
        null,
        DateTimeOffset.MinValue);

    public double CompletionRate => Tasks == 0 ? 0 : (double)CompletedTasks / Tasks;
}
