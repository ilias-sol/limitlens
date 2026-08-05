namespace LimitLens.Core.Models;

public sealed record TokenUsageBreakdown(
    long InputTokens = 0,
    long CachedInputTokens = 0,
    long CacheWriteInputTokens = 0,
    long OutputTokens = 0,
    long ReasoningOutputTokens = 0,
    long TotalTokens = 0)
{
    public static TokenUsageBreakdown Empty { get; } = new();

    public TokenUsageBreakdown Add(TokenUsageBreakdown other) => new(
        InputTokens + other.InputTokens,
        CachedInputTokens + other.CachedInputTokens,
        CacheWriteInputTokens + other.CacheWriteInputTokens,
        OutputTokens + other.OutputTokens,
        ReasoningOutputTokens + other.ReasoningOutputTokens,
        TotalTokens + other.TotalTokens);

    public TokenUsageBreakdown PositiveDeltaFrom(TokenUsageBreakdown previous) => new(
        PositiveDelta(InputTokens, previous.InputTokens),
        PositiveDelta(CachedInputTokens, previous.CachedInputTokens),
        PositiveDelta(CacheWriteInputTokens, previous.CacheWriteInputTokens),
        PositiveDelta(OutputTokens, previous.OutputTokens),
        PositiveDelta(ReasoningOutputTokens, previous.ReasoningOutputTokens),
        PositiveDelta(TotalTokens, previous.TotalTokens));

    private static long PositiveDelta(long current, long previous) =>
        current >= previous ? current - previous : current;
}
