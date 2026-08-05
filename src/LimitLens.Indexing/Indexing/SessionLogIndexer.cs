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
    private readonly CodexPathResolver pathResolver = new(settings);
    private readonly SessionLogParser parser = new(settings.PrivacySalt);
    private readonly ConcurrentDictionary<string, SessionAggregate> sessions =
        new(StringComparer.Ordinal);
    private readonly Channel<string> changeQueue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly SemaphoreSlim scanGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? changeWorker;
    private Task? reconciliationWorker;
    private string? codexHome;
    private volatile bool indexingEnabled = true;
    private bool disposed;

    public LocalUsageAggregate Current { get; private set; } = LocalUsageAggregate.Empty;
    public SourceHealth Health { get; private set; } = SourceHealth.Starting("Preparing local analytics…");

    public event Action<LocalUsageAggregate>? SnapshotChanged;
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

        lifetime.Dispose();
        scanGate.Dispose();
    }

    private async Task ScanAllAsync(CancellationToken cancellationToken)
    {
        await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var files = EnumerateSessionFiles().OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
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

            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ProcessFileCoreAsync(files[index], cancellationToken).ConfigureAwait(false);
                BackfillProgressChanged?.Invoke((double)(index + 1) / files.Length);
            }

            await PublishAggregateAsync(cancellationToken).ConfigureAwait(false);
            SetHealth(new SourceHealth(
                SourceConnectionState.Connected,
                "Local Codex analytics are up to date.",
                DateTimeOffset.Now));
        }
        finally
        {
            scanGate.Release();
        }
    }

    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        var pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await changeQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
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

            await scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var path in pending)
                {
                    if (File.Exists(path))
                    {
                        await ProcessFileCoreAsync(path, cancellationToken).ConfigureAwait(false);
                    }
                }

                pending.Clear();
                await PublishAggregateAsync(cancellationToken).ConfigureAwait(false);
                SetHealth(new SourceHealth(
                    SourceConnectionState.Connected,
                    "Local Codex analytics are live.",
                    DateTimeOffset.Now));
            }
            catch (IOException exception)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Faulted,
                    "A session file could not be read yet; it will be retried.",
                    Health.LastSuccessfulUpdate,
                    exception.GetType().Name));
            }
            finally
            {
                scanGate.Release();
            }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!indexingEnabled)
            {
                continue;
            }

            foreach (var path in EnumerateSessionFiles())
            {
                changeQueue.Writer.TryWrite(path);
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

    private static bool IsUnderHome(string path, string home)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedHome, StringComparison.OrdinalIgnoreCase);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
