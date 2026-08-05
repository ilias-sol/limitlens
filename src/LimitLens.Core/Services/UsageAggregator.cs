using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;

namespace LimitLens.Core.Services;

public static class UsageAggregator
{
    public static LocalUsageAggregate Aggregate(
        IEnumerable<SessionAggregate> sessions,
        DateTimeOffset? now = null,
        TimeZoneInfo? timeZone = null)
    {
        var materialized = sessions.ToArray();
        var turns = materialized.SelectMany(session => session.Turns).ToArray();
        var zone = timeZone ?? TimeZoneInfo.Local;
        var clock = now ?? DateTimeOffset.Now;

        var totalTokens = turns.Aggregate(
            TokenUsageBreakdown.Empty,
            static (total, turn) => total.Add(turn.Tokens));

        var daily = turns
            .GroupBy(turn => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(turn.StartedAt, zone).DateTime))
            .OrderBy(group => group.Key)
            .Select(group => new DailyLocalUsage(
                group.Key,
                group.Aggregate(TokenUsageBreakdown.Empty, static (total, turn) => total.Add(turn.Tokens)),
                group.Count(),
                group.Count(turn => turn.Outcome == TaskOutcome.Completed),
                group.Sum(turn => turn.DurationMilliseconds)))
            .ToArray();

        var projects = turns
            .GroupBy(turn => new { turn.ProjectId, turn.ProjectDisplayName })
            .Select(group => new ProjectUsage(
                group.Key.ProjectId,
                group.Key.ProjectDisplayName,
                group.Aggregate(TokenUsageBreakdown.Empty, static (total, turn) => total.Add(turn.Tokens)),
                group.Count(),
                group.Sum(turn => turn.DurationMilliseconds)))
            .OrderByDescending(project => project.Tokens.TotalTokens)
            .ThenBy(project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var models = turns
            .GroupBy(turn => string.IsNullOrWhiteSpace(turn.Model) ? "Unknown" : turn.Model)
            .Select(group => new ModelUsage(
                group.Key!,
                group.Aggregate(TokenUsageBreakdown.Empty, static (total, turn) => total.Add(turn.Tokens)),
                group.Count()))
            .OrderByDescending(model => model.Tokens.TotalTokens)
            .ToArray();

        var tools = turns
            .SelectMany(turn => turn.ToolCounts)
            .GroupBy(item => item.Key)
            .Select(group => new ToolCategoryUsage(group.Key, group.Sum(item => item.Value)))
            .OrderByDescending(item => item.Count)
            .ToArray();

        var activeTurn = turns
            .Where(turn => turn.Outcome == TaskOutcome.InProgress)
            .OrderByDescending(turn => turn.StartedAt)
            .FirstOrDefault();

        CurrentTaskSnapshot? current = activeTurn is null
            ? null
            : new CurrentTaskSnapshot(
                activeTurn.SessionId,
                activeTurn.TurnId,
                activeTurn.ProjectDisplayName,
                activeTurn.Model,
                activeTurn.Tokens,
                activeTurn.StartedAt,
                clock - activeTurn.StartedAt,
                activeTurn.Outcome);

        var ttftSamples = turns
            .Where(turn => turn.TimeToFirstTokenMilliseconds > 0)
            .Select(turn => turn.TimeToFirstTokenMilliseconds)
            .ToArray();

        return new LocalUsageAggregate(
            totalTokens,
            materialized.Select(session => session.SessionId).Distinct(StringComparer.Ordinal).Count(),
            turns.Length,
            turns.Count(turn => turn.Outcome == TaskOutcome.Completed),
            turns.Count(turn => turn.Outcome == TaskOutcome.Aborted),
            turns.Sum(turn => turn.DurationMilliseconds),
            ttftSamples.Length == 0 ? 0 : (long)ttftSamples.Average(),
            daily,
            projects,
            models,
            tools,
            current,
            clock);
    }
}
