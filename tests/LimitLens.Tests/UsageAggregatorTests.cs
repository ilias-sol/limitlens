using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Services;

namespace LimitLens.Tests;

public sealed class UsageAggregatorTests
{
    [Fact]
    public void AggregatesProjectsModelsOutcomesAndLocalDays()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
        var turns = new[]
        {
            Turn("one", "Project A", "gpt-5.6", new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero), 100, TaskOutcome.Completed),
            Turn("two", "Project A", "gpt-5.6", new DateTimeOffset(2026, 3, 29, 22, 30, 0, TimeSpan.Zero), 200, TaskOutcome.Aborted),
            Turn("three", "Project B", "gpt-5.5", new DateTimeOffset(2026, 3, 30, 8, 0, 0, TimeSpan.Zero), 300, TaskOutcome.Completed),
        };
        var sessions = new[]
        {
            new SessionAggregate("session", "project", "Project A", turns[0].StartedAt, turns[^1].StartedAt, turns, TokenUsageBreakdown.Empty, "safe.jsonl"),
        };

        var aggregate = UsageAggregator.Aggregate(sessions, new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.Zero), zone);

        Assert.Equal(600, aggregate.Tokens.TotalTokens);
        Assert.Equal(3, aggregate.Tasks);
        Assert.Equal(2, aggregate.CompletedTasks);
        Assert.Equal(1, aggregate.AbortedTasks);
        Assert.Equal(2d / 3d, aggregate.CompletionRate, 6);
        Assert.Equal(2, aggregate.Projects.Count);
        Assert.Equal(2, aggregate.Models.Count);
        Assert.Equal(2, aggregate.DailyUsage.Count);
        Assert.Equal(new DateOnly(2026, 3, 29), aggregate.DailyUsage[0].Date);
        Assert.Equal(new DateOnly(2026, 3, 30), aggregate.DailyUsage[^1].Date);
    }

    [Fact]
    public void DoesNotInventAccountTotals()
    {
        var session = new SessionAggregate(
            "session",
            "project",
            "Project",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [Turn("one", "Project", "gpt-5.6", DateTimeOffset.UnixEpoch, 123, TaskOutcome.Completed)],
            TokenUsageBreakdown.Empty,
            "safe.jsonl");

        var aggregate = UsageAggregator.Aggregate([session], DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc);

        Assert.Equal(123, aggregate.Tokens.TotalTokens);
        Assert.Equal(123, Assert.Single(aggregate.DailyUsage).Tokens.TotalTokens);
    }

    private static TurnAggregate Turn(
        string id,
        string project,
        string model,
        DateTimeOffset started,
        long tokens,
        TaskOutcome outcome) => new(
            id,
            "session",
            project,
            project,
            model,
            started,
            started.AddSeconds(10),
            outcome,
            new TokenUsageBreakdown(tokens, 0, 0, 0, 0, tokens),
            10_000,
            500,
            new Dictionary<ToolCategory, int> { [ToolCategory.Shell] = 1 });
}
