using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.Core.Abstractions;

public interface ICodexAppServerClient : IAsyncDisposable
{
    AccountUsageSnapshot Current { get; }
    SourceHealth Health { get; }
    event Action<AccountUsageSnapshot>? SnapshotChanged;
    event Action<SourceHealth>? HealthChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public interface ISessionLogIndexer : IAsyncDisposable
{
    LocalUsageAggregate Current { get; }
    IReadOnlyList<UsageHistorySample> RateLimitHistory { get; }
    SourceHealth Health { get; }
    event Action<LocalUsageAggregate>? SnapshotChanged;
    event Action? RateLimitHistoryInvalidated;
    event Action<SourceHealth>? HealthChanged;
    event Action<double>? BackfillProgressChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RebuildAsync(CancellationToken cancellationToken = default);
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}

public interface IUsageRepository : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ReplaceSessionAsync(SessionAggregate session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SessionAggregate>> LoadSessionsAsync(CancellationToken cancellationToken = default);
    Task<LocalUsageAggregate> LoadAggregateAsync(CancellationToken cancellationToken = default);
    Task<FileCheckpoint?> GetCheckpointAsync(string fileKey, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(FileCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    DashboardSettings Current { get; }
    Task<DashboardSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DashboardSettings settings, CancellationToken cancellationToken = default);
}

public sealed record FileCheckpoint(
    string FileKey,
    string Path,
    long Offset,
    long Length,
    DateTimeOffset LastWriteTime,
    string? SessionId);

public sealed record TurnAggregate(
    string TurnId,
    string SessionId,
    string ProjectId,
    string ProjectDisplayName,
    string? Model,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TaskOutcome Outcome,
    TokenUsageBreakdown Tokens,
    long DurationMilliseconds,
    long TimeToFirstTokenMilliseconds,
    IReadOnlyDictionary<ToolCategory, int> ToolCounts);

public sealed record SessionAggregate(
    string SessionId,
    string ProjectId,
    string ProjectDisplayName,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TurnAggregate> Turns,
    TokenUsageBreakdown LastCumulativeTokens,
    string SourcePath);
