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
