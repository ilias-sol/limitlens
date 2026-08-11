using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;
using LimitLens.Indexing.Indexing;

namespace LimitLens.Tests;

public sealed class SessionLogIndexerTests
{
    private const string Salt = "00112233445566778899AABBCCDDEEFF";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    [Fact]
    public async Task BackfillsAndCompactsPrimaryRateLimitHistoryFromSessionMetadata()
    {
        using var folder = new TempFolder();
        var sessions = Directory.CreateDirectory(folder.GetPath("sessions"));
        var repository = new SelectivelyFailingRepository("never");
        var reset = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddDays(6).ToUnixTimeSeconds());
        var start = reset - TimeSpan.FromDays(7);
        var lines = new[]
        {
            JsonSerializer.Serialize(new
            {
                timestamp = start,
                type = "session_meta",
                payload = new { id = "history-session", timestamp = start, cwd = @"C:\Work\SafeProject" },
            }),
            RateLimitLine(start, 0, reset.AddSeconds(-6)),
            RateLimitLine(start.AddHours(1), 20, reset),
            RateLimitLine(start.AddHours(2), 20, reset),
            RateLimitLine(start.AddHours(3), 20, reset),
            RateLimitLine(start.AddHours(20), 40, reset),
        };
        await File.WriteAllLinesAsync(
            Path.Combine(sessions.FullName, "history.jsonl"),
            lines,
            Utf8NoBom);

        await using var indexer = new SessionLogIndexer(
            repository,
            new DashboardSettings
            {
                CodexHomePath = folder.Path,
                PrivacySalt = Salt,
            });

        await indexer.StartAsync();

        Assert.Equal([100, 80, 80, 60], indexer.RateLimitHistory.Select(sample => sample.RemainingPercent));
        Assert.All(indexer.RateLimitHistory, sample => Assert.Equal(reset, sample.ResetAt));
        Assert.Equal(start.AddHours(1), indexer.RateLimitHistory[1].Timestamp);
        Assert.Equal(start.AddHours(3), indexer.RateLimitHistory[2].Timestamp);
    }

    [Fact]
    public async Task RebuildsRateLimitHistoryWhenSessionFileIsTruncated()
    {
        using var folder = new TempFolder();
        var sessions = Directory.CreateDirectory(folder.GetPath("sessions"));
        var path = Path.Combine(sessions.FullName, "history.jsonl");
        var reset = DateTimeOffset.UtcNow.AddDays(6);
        var start = reset - TimeSpan.FromDays(7);
        var metadata = JsonSerializer.Serialize(new
        {
            timestamp = start,
            type = "session_meta",
            payload = new { id = "history-session", timestamp = start, cwd = @"C:\Work\SafeProject" },
        });
        await File.WriteAllLinesAsync(
            path,
            new[]
            {
                metadata,
                RateLimitLine(start, 0, reset),
                RateLimitLine(start.AddHours(1), 20, reset),
                RateLimitLine(start.AddHours(2), 40, reset),
                RateLimitLine(start.AddHours(3), 60, reset),
            },
            Utf8NoBom);

        await using var indexer = new SessionLogIndexer(
            new SelectivelyFailingRepository("never"),
            new DashboardSettings
            {
                CodexHomePath = folder.Path,
                PrivacySalt = Salt,
            });
        var invalidationCount = 0;
        indexer.RateLimitHistoryInvalidated += () => Interlocked.Increment(ref invalidationCount);

        await indexer.StartAsync();
        Assert.Contains(indexer.RateLimitHistory, sample => sample.Timestamp == start.AddHours(3));

        await File.WriteAllLinesAsync(
            path,
            new[]
            {
                metadata,
                RateLimitLine(start, 0, reset),
                RateLimitLine(start.AddHours(1), 30, reset),
            },
            Utf8NoBom);

        await WaitUntilAsync(() =>
            Volatile.Read(ref invalidationCount) > 0 &&
            indexer.RateLimitHistory.All(sample => sample.Timestamp < start.AddHours(2)) &&
            indexer.RateLimitHistory.Any(sample => sample.Timestamp == start.AddHours(1) && sample.RemainingPercent == 70));

        Assert.DoesNotContain(indexer.RateLimitHistory, sample => sample.Timestamp == start.AddHours(3));
    }

    [Fact]
    public async Task LiveWorkerContinuesAfterOneSessionFileFails()
    {
        using var folder = new TempFolder();
        var sessions = Directory.CreateDirectory(folder.GetPath("sessions"));
        var repository = new SelectivelyFailingRepository("SESSIONS/BAD.JSONL");
        await using var indexer = new SessionLogIndexer(
            repository,
            new DashboardSettings
            {
                CodexHomePath = folder.Path,
                PrivacySalt = Salt,
            });
        await indexer.StartAsync();

        await WriteSessionAsync(Path.Combine(sessions.FullName, "bad.jsonl"), "bad-session");
        await WaitUntilAsync(() => indexer.Health.State == SourceConnectionState.Faulted);

        await WriteSessionAsync(Path.Combine(sessions.FullName, "good.jsonl"), "good-session");
        await WaitUntilAsync(() => repository.ReplacedSessions.Any(session => session.SessionId == "good-session"));

        Assert.Contains(repository.ReplacedSessions, session => session.SessionId == "good-session");
    }

    private static Task WriteSessionAsync(string path, string sessionId)
    {
        var record = JsonSerializer.Serialize(new
        {
            timestamp = "2026-08-04T08:00:00Z",
            type = "session_meta",
            payload = new
            {
                id = sessionId,
                timestamp = "2026-08-04T08:00:00Z",
                cwd = @"C:\Work\SafeProject",
            },
        });
        return File.WriteAllTextAsync(path, record + Environment.NewLine, Utf8NoBom);
    }

    private static string RateLimitLine(
        DateTimeOffset timestamp,
        int usedPercent,
        DateTimeOffset reset) => JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new
                    {
                        input_tokens = 0,
                        cached_input_tokens = 0,
                        output_tokens = 0,
                        reasoning_output_tokens = 0,
                        total_tokens = 0,
                    },
                },
                rate_limits = new
                {
                    primary = new
                    {
                        used_percent = usedPercent,
                        window_minutes = 10_080,
                        resets_at = reset.ToUnixTimeSeconds(),
                    },
                },
            },
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the indexer worker.");
            await Task.Delay(25);
        }
    }

    private sealed class SelectivelyFailingRepository(string failingFileKey) : IUsageRepository
    {
        public ConcurrentQueue<SessionAggregate> ReplacedSessions { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReplaceSessionAsync(
            SessionAggregate session,
            CancellationToken cancellationToken = default)
        {
            ReplacedSessions.Enqueue(session);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionAggregate>> LoadSessionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionAggregate>>([]);

        public Task<LocalUsageAggregate> LoadAggregateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalUsageAggregate.Empty);

        public Task<FileCheckpoint?> GetCheckpointAsync(
            string fileKey,
            CancellationToken cancellationToken = default) =>
            fileKey.Equals(failingFileKey, StringComparison.Ordinal)
                ? Task.FromException<FileCheckpoint?>(new InvalidOperationException("Simulated repository failure."))
                : Task.FromResult<FileCheckpoint?>(null);

        public Task SaveCheckpointAsync(
            FileCheckpoint checkpoint,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
