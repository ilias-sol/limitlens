using System.Collections.Concurrent;
using System.Threading.Channels;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;
using LimitLens.Indexing.Parsing;
using LimitLens.Indexing.Privacy;

namespace LimitLens.Indexing.Indexing;

public sealed class SessionLogIndexer(
    IUsageRepository repository,
    DashboardSettings settings) : ISessionLogIndexer
{
    private const int MaxQueuedChanges = 4096;
    private const int MaxRateLimitHistorySamples = 512;
    private static readonly TimeSpan MaxRateLimitHistoryAge = TimeSpan.FromDays(8);
    private static readonly TimeSpan ResetTimestampTolerance = TimeSpan.FromMinutes(2);
    private readonly CodexPathResolver pathResolver = new(settings);
    private readonly SessionLogParser parser = new(settings.PrivacySalt);
    private readonly ConcurrentDictionary<string, SessionAggregate> sessions =
        new(StringComparer.Ordinal);
    private readonly Channel<string> changeQueue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(MaxQueuedChanges)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Dictionary<string, RateLimitFileState> rateLimitFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? changeWorker;
    private Task? reconciliationWorker;
    private string? codexHome;
    private IReadOnlyList<UsageHistorySample> rateLimitHistory = [];
    private volatile bool indexingEnabled = true;
    private bool disposed;

    public LocalUsageAggregate Current { get; private set; } = LocalUsageAggregate.Empty;
    public IReadOnlyList<UsageHistorySample> RateLimitHistory => rateLimitHistory;
    public SourceHealth Health { get; private set; } = SourceHealth.Starting("Preparing local analytics…");

    public event Action<LocalUsageAggregate>? SnapshotChanged;
    public event Action? RateLimitHistoryInvalidated;
    public event Action<SourceHealth>? HealthChanged;
    public event Action<double>? BackfillProgressChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        codexHome = Path.GetFullPath(pathResolver.ResolveHome());
        await repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        foreach (var session in await repository.LoadSessionsAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions[session.SessionId] = session;
        }

        SetHealth(SourceHealth.Starting("Indexing Codex sessions…"));
        await ScanAllAsync(cancellationToken).ConfigureAwait(false);
        SetUpWatchers();
        changeWorker = Task.Run(() => ProcessChangesAsync(lifetime.Token), CancellationToken.None);
        reconciliationWorker = Task.Run(() => ReconcileAsync(lifetime.Token), CancellationToken.None);
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        indexingEnabled = true;
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetHealth(SourceHealth.Starting("Rebuilding local analytics…"));
            await repository.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
            sessions.Clear();
            rateLimitFiles.Clear();
            rateLimitHistory = [];
        }
        finally
        {
            scanGate.Release();
        }

        await ScanAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        indexingEnabled = false;
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
            sessions.Clear();
            rateLimitFiles.Clear();
            rateLimitHistory = [];
            Current = LocalUsageAggregate.Empty;
            SnapshotChanged?.Invoke(Current);
            SetHealth(new SourceHealth(
                SourceConnectionState.LocalOnly,
                "Local analytics were deleted. Rebuild when you want to scan again."));
        }
        finally
        {
            scanGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        changeQueue.Writer.TryComplete();
        var workers = new[] { changeWorker, reconciliationWorker }.Where(task => task is not null).Cast<Task>();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Background failures are surfaced through Health; shutdown must remain best-effort.
        }

        lifetime.Dispose();
        scanGate.Dispose();
    }

    private async Task ScanAllAsync(CancellationToken cancellationToken)
    {
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var files = EnumerateSessionFiles().OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (PruneRateLimitHistoryFiles(files))
            {
                RateLimitHistoryInvalidated?.Invoke();
            }

            if (files.Length == 0)
            {
                Current = await repository.LoadAggregateAsync(cancellationToken).ConfigureAwait(false);
                SnapshotChanged?.Invoke(Current);
                SetHealth(new SourceHealth(
                    SourceConnectionState.Missing,
                    "No Codex session files were found.",
                    null,
                    "Check the Codex home path in Settings."));
                return;
            }

            var failedFiles = 0;
            Exception? latestFailure = null;
            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var failure = await TryProcessFileAsync(files[index], cancellationToken).ConfigureAwait(false);
                var historyFailure = await TryProcessRateLimitHistoryFileAsync(
                    files[index],
                    fromBeginning: true,
                    cancellationToken).ConfigureAwait(false);
                failure ??= historyFailure;
                if (failure is not null)
                {
                    failedFiles++;
                    latestFailure = failure;
                }

                BackfillProgressChanged?.Invoke((double)(index + 1) / files.Length);
            }

            await PublishAggregateAsync(cancellationToken).ConfigureAwait(false);
            if (latestFailure is null)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Connected,
                    "Local Codex analytics are up to date.",
                    DateTimeOffset.Now));
            }
            else
            {
                SetFileFailureHealth(failedFiles, latestFailure);
            }
        }
        finally
        {
            scanGate.Release();
        }
    }

    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await changeQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                while (changeQueue.Reader.TryRead(out var path))
                {
                    pending.Add(path);
                }

                if (!indexingEnabled)
                {
                    pending.Clear();
                    continue;
                }

                await Task.Delay(350, cancellationToken).ConfigureAwait(false);
                while (changeQueue.Reader.TryRead(out var path))
                {
                    pending.Add(path);
                }

                await ProcessPendingChangesAsync(pending, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                pending.Clear();
                SetHealth(new SourceHealth(
                    SourceConnectionState.Faulted,
                    "Live session indexing hit an unexpected error; reconciliation will retry.",
                    Health.LastSuccessfulUpdate,
                    exception.GetType().Name));
            }
        }
    }

    private async Task ProcessPendingChangesAsync(
        HashSet<string> pending,
        CancellationToken cancellationToken)
    {
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!indexingEnabled)
            {
                pending.Clear();
                return;
            }

            if (PruneRateLimitHistoryFiles(EnumerateSessionFiles()))
            {
                RateLimitHistoryInvalidated?.Invoke();
            }

            var paths = pending.ToArray();
            pending.Clear();
            var failedFiles = 0;
            Exception? latestFailure = null;
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var failure = await TryProcessFileAsync(path, cancellationToken).ConfigureAwait(false);
                var historyFailure = await TryProcessRateLimitHistoryFileAsync(
                    path,
                    fromBeginning: false,
                    cancellationToken).ConfigureAwait(false);
                failure ??= historyFailure;
                if (failure is not null)
                {
                    failedFiles++;
                    latestFailure = failure;
                }
            }

            await PublishAggregateAsync(cancellationToken).ConfigureAwait(false);
            if (latestFailure is null)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Connected,
                    "Local Codex analytics are live.",
                    DateTimeOffset.Now));
            }
            else
            {
                SetFileFailureHealth(failedFiles, latestFailure);
            }
        }
        finally
        {
            scanGate.Release();
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                if (!indexingEnabled)
                {
                    continue;
                }

                foreach (var path in EnumerateSessionFiles())
                {
                    changeQueue.Writer.TryWrite(path);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Faulted,
                    "Session reconciliation failed; the next scan will retry.",
                    Health.LastSuccessfulUpdate,
                    exception.GetType().Name));
            }
        }
    }

    private async Task ProcessFileCoreAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (codexHome is null || !IsUnderHome(fullPath, codexHome))
        {
            return;
        }

        var information = new FileInfo(fullPath);
        if (!information.Exists)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(codexHome, information.FullName);
        var fileKey = relativePath.Replace('\\', '/').ToUpperInvariant();
        var checkpoint = await repository.GetCheckpointAsync(fileKey, cancellationToken).ConfigureAwait(false);
        var lastWrite = new DateTimeOffset(information.LastWriteTimeUtc, TimeSpan.Zero);

        if (checkpoint is not null &&
            checkpoint.Length == information.Length &&
            checkpoint.LastWriteTime >= lastWrite)
        {
            return;
        }

        SessionAggregate? existing = null;
        if (checkpoint?.SessionId is not null)
        {
            sessions.TryGetValue(checkpoint.SessionId, out existing);
        }

        var truncated = checkpoint is not null && information.Length < checkpoint.Offset;
        var startOffset = truncated ? 0 : checkpoint?.Offset ?? 0;
        if (truncated)
        {
            existing = null;
        }

        var result = await parser.ParseAsync(
            information.FullName,
            relativePath,
            existing,
            startOffset,
            cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return;
        }

        sessions[result.Session.SessionId] = result.Session;
        await repository.ReplaceSessionAsync(result.Session, cancellationToken).ConfigureAwait(false);
        information.Refresh();
        await repository.SaveCheckpointAsync(
            new FileCheckpoint(
                fileKey,
                relativePath,
                result.CompletedOffset,
                information.Exists ? information.Length : result.CompletedOffset,
                information.Exists
                    ? new DateTimeOffset(information.LastWriteTimeUtc, TimeSpan.Zero)
                    : lastWrite,
                result.Session.SessionId),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Exception?> TryProcessFileAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessFileCoreAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task ProcessRateLimitHistoryFileCoreAsync(
        string fullPath,
        bool fromBeginning,
        CancellationToken cancellationToken)
    {
        if (codexHome is null || !IsUnderHome(fullPath, codexHome))
        {
            return;
        }

        var information = new FileInfo(fullPath);
        if (!information.Exists)
        {
            return;
        }

        if (!fromBeginning &&
            !rateLimitFiles.ContainsKey(fullPath) &&
            information.LastWriteTimeUtc < DateTime.UtcNow - MaxRateLimitHistoryAge)
        {
            return;
        }

        var previous = rateLimitFiles.GetValueOrDefault(fullPath);
        var rewound = previous is not null &&
            (information.Length < previous.Length ||
             information.Length < previous.CompletedOffset ||
             information.CreationTimeUtc != previous.CreationTimeUtc ||
             information.LastWriteTimeUtc < previous.LastWriteTimeUtc);
        var startOffset = fromBeginning || rewound ? 0 : previous?.CompletedOffset ?? 0;
        var result = await RateLimitHistoryParser.ParseAsync(
            fullPath,
            startOffset,
            cancellationToken).ConfigureAwait(false);

        var fileSamples = (rewound ? [] : previous?.Samples ?? [])
            .Concat(result.Samples);
        rateLimitFiles[fullPath] = new RateLimitFileState(
            result.CompletedOffset,
            information.Length,
            information.LastWriteTimeUtc,
            information.CreationTimeUtc,
            CompactRateLimitHistory(fileSamples));
        rateLimitHistory = CompactRateLimitHistory(
            rateLimitFiles.Values.SelectMany(file => file.Samples));
        if (rewound)
        {
            RateLimitHistoryInvalidated?.Invoke();
        }
    }

    private bool PruneRateLimitHistoryFiles(IEnumerable<string> existingPaths)
    {
        var existing = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = rateLimitFiles.Keys
            .Where(path => !existing.Contains(path))
            .ToArray();
        if (removed.Length == 0)
        {
            return false;
        }

        foreach (var path in removed)
        {
            rateLimitFiles.Remove(path);
        }

        rateLimitHistory = CompactRateLimitHistory(
            rateLimitFiles.Values.SelectMany(file => file.Samples));
        return true;
    }

    private async Task<Exception?> TryProcessRateLimitHistoryFileAsync(
        string fullPath,
        bool fromBeginning,
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessRateLimitHistoryFileCoreAsync(
                fullPath,
                fromBeginning,
                cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static IReadOnlyList<UsageHistorySample> CompactRateLimitHistory(
        IEnumerable<UsageHistorySample> samples)
    {
        var cutoff = DateTimeOffset.UtcNow - MaxRateLimitHistoryAge;
        var candidates = samples
            .Where(sample => sample.Timestamp >= cutoff &&
                             sample.Timestamp <= DateTimeOffset.UtcNow.AddMinutes(1) &&
                             sample.ResetAt > sample.Timestamp &&
                             sample.RemainingPercent is >= 0 and <= 100)
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        return candidates
            .GroupBy(sample => (sample.LimitId, sample.WindowDurationMinutes))
            .SelectMany(group => CompactWindowHistory(group.ToArray()))
            .OrderBy(sample => sample.Timestamp)
            .TakeLast(MaxRateLimitHistorySamples)
            .ToArray();
    }

    private static IEnumerable<UsageHistorySample> CompactWindowHistory(UsageHistorySample[] candidates)
    {
        var resetGroups = candidates
            .GroupBy(sample => sample.ResetAt)
            .Select(group => new ResetGroup(group.Key, group.Count()))
            .OrderBy(group => group.ResetAt)
            .ToArray();
        var canonicalResets = new Dictionary<DateTimeOffset, DateTimeOffset>();
        for (var index = 0; index < resetGroups.Length;)
        {
            var end = index + 1;
            while (end < resetGroups.Length &&
                   resetGroups[end].ResetAt - resetGroups[end - 1].ResetAt <= ResetTimestampTolerance)
            {
                end++;
            }

            var canonical = resetGroups[index..end]
                .OrderByDescending(group => group.Count)
                .ThenByDescending(group => group.ResetAt)
                .First()
                .ResetAt;
            for (var groupIndex = index; groupIndex < end; groupIndex++)
            {
                canonicalResets[resetGroups[groupIndex].ResetAt] = canonical;
            }

            index = end;
        }

        var compacted = new List<UsageHistorySample>();
        foreach (var window in candidates
                     .Select(sample => new UsageHistorySample
                     {
                         Timestamp = sample.Timestamp,
                         RemainingPercent = sample.RemainingPercent,
                         ResetAt = canonicalResets[sample.ResetAt],
                         LimitId = sample.LimitId,
                         WindowDurationMinutes = sample.WindowDurationMinutes,
                     })
                     .GroupBy(sample => sample.ResetAt)
                     .OrderBy(group => group.Key))
        {
            var windowSamples = new List<UsageHistorySample>();
            foreach (var sample in window
                         .GroupBy(candidate => candidate.Timestamp)
                         .Select(group => group.OrderBy(candidate => candidate.RemainingPercent).First())
                         .OrderBy(candidate => candidate.Timestamp))
            {
                if (windowSamples.Count > 0 &&
                    sample.RemainingPercent > windowSamples[^1].RemainingPercent)
                {
                    continue;
                }

                if (windowSamples.Count > 1 &&
                    sample.RemainingPercent == windowSamples[^1].RemainingPercent &&
                    sample.RemainingPercent == windowSamples[^2].RemainingPercent)
                {
                    windowSamples[^1] = sample;
                }
                else
                {
                    windowSamples.Add(sample);
                }
            }

            compacted.AddRange(windowSamples);
        }

        return compacted;
    }

    private sealed record RateLimitFileState(
        long CompletedOffset,
        long Length,
        DateTime LastWriteTimeUtc,
        DateTime CreationTimeUtc,
        IReadOnlyList<UsageHistorySample> Samples);

    private sealed record ResetGroup(DateTimeOffset ResetAt, int Count);

    private async Task PublishAggregateAsync(CancellationToken cancellationToken)
    {
        Current = await repository.LoadAggregateAsync(cancellationToken).ConfigureAwait(false);
        SnapshotChanged?.Invoke(Current);
    }

    private IEnumerable<string> EnumerateSessionFiles()
    {
        if (codexHome is null)
        {
            return [];
        }

        var roots = new[]
        {
            Path.Combine(codexHome, "sessions"),
            Path.Combine(codexHome, "archived_sessions"),
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*.jsonl",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }));
    }

    private void SetUpWatchers()
    {
        if (codexHome is null)
        {
            return;
        }

        foreach (var root in new[]
        {
            Path.Combine(codexHome, "sessions"),
            Path.Combine(codexHome, "archived_sessions"),
        }.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime,
                InternalBufferSize = 32 * 1024,
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs args) =>
        changeQueue.Writer.TryWrite(args.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs args) =>
        changeQueue.Writer.TryWrite(args.FullPath);

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        SetHealth(new SourceHealth(
            SourceConnectionState.Faulted,
            "The live session watcher missed events; a reconciliation scan will recover them.",
            Health.LastSuccessfulUpdate,
            args.GetException().GetType().Name));
    }

    private void SetHealth(SourceHealth health)
    {
        Health = health;
        HealthChanged?.Invoke(health);
    }

    private void SetFileFailureHealth(int failedFiles, Exception latestFailure)
    {
        var noun = failedFiles == 1 ? "file" : "files";
        SetHealth(new SourceHealth(
            SourceConnectionState.Faulted,
            $"{failedFiles} session {noun} could not be indexed; reconciliation will retry.",
            Health.LastSuccessfulUpdate,
            latestFailure.GetType().Name));
    }

    private static bool IsUnderHome(string path, string home)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedHome, StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
