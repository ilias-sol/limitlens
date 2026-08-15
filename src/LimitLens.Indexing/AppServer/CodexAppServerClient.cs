using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;
using LimitLens.Indexing.Privacy;

namespace LimitLens.Indexing.AppServer;

public sealed class CodexAppServerClient(DashboardSettings settings) : ICodexAppServerClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CodexPathResolver pathResolver = new(settings);
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly SemaphoreSlim reconnectSignal = new(0, 1);
    private readonly object stateGate = new();
    private readonly Dictionary<string, RateLimitBucket> rateLimits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> extensions = new(StringComparer.Ordinal);
    private Process? process;
    private StreamWriter? input;
    private Task? supervisor;
    private long nextRequestId;
    private string? planType;
    private AccountUsageSummary accountSummary = new();
    private IReadOnlyList<DailyTokenUsage> dailyUsage = [];
    private ResetCreditSummary? resetCredits;
    private bool accountAvailable;
    private bool disposed;

    public AccountUsageSnapshot Current { get; private set; } = AccountUsageSnapshot.Empty;
    public SourceHealth Health { get; private set; } = SourceHealth.Starting("Connecting to Codex…");

    public event Action<AccountUsageSnapshot>? SnapshotChanged;
    public event Action<SourceHealth>? HealthChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        supervisor ??= Task.Run(() => SuperviseAsync(lifetime.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (input is null || process is null || process.HasExited)
        {
            SignalReconnect();
            SetHealth(new SourceHealth(
                SourceConnectionState.Offline,
                "Codex account data is temporarily unavailable.",
                Health.LastSuccessfulUpdate));
            return;
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accountResult = await SendRequestAsync(
                "account/read",
                new { refreshToken = false },
                cancellationToken).ConfigureAwait(false);
            var accountAvailable = ApplyAccount(accountResult);
            if (!accountAvailable)
            {
                PublishSnapshot();
                return;
            }

            var usageResult = await SendRequestAsync(
                "account/usage/read",
                null,
                cancellationToken).ConfigureAwait(false);
            ApplyUsage(usageResult);

            var rateLimitResult = await SendRequestAsync(
                "account/rateLimits/read",
                null,
                cancellationToken).ConfigureAwait(false);
            ApplyRateLimits(rateLimitResult);
            PublishSnapshot();
            SetHealth(new SourceHealth(
                SourceConnectionState.Connected,
                "Connected to Codex account usage.",
                DateTimeOffset.Now));
        }
        finally
        {
            refreshGate.Release();
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
        TryStopProcess();
        if (supervisor is not null)
        {
            try
            {
                await supervisor.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        FailPending(new OperationCanceledException("Codex app-server client stopped."));
        lifetime.Dispose();
        writeGate.Dispose();
        refreshGate.Dispose();
        reconnectSignal.Dispose();
        SetHealth(new SourceHealth(SourceConnectionState.Stopped, "Codex account connection stopped."));
    }

    private async Task SuperviseAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                SetHealth(SourceHealth.Starting("Starting Codex app-server…"));
                await RunConnectionAsync(cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(1);
            }
            catch (Win32Exception)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Missing,
                    "Codex was not found. Local analytics are still available.",
                    Health.LastSuccessfulUpdate,
                    "Choose the Codex executable in Settings."));
                retryDelay = TimeSpan.FromSeconds(30);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SetHealth(new SourceHealth(
                    SourceConnectionState.Offline,
                    "Codex account data is offline; local analytics remain available.",
                    Health.LastSuccessfulUpdate,
                    exception.GetType().Name));
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
            }
            finally
            {
                TryStopProcess();
                FailPending(new IOException("Codex app-server connection closed."));
            }

            await WaitForRetryOrSignalAsync(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pathResolver.ResolveExecutable(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--listen");
        startInfo.ArgumentList.Add("stdio://");

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Codex app-server could not be started.");
        }

        input = process.StandardInput;
        input.AutoFlush = true;
        var outputTask = ReadOutputAsync(process.StandardOutput, cancellationToken);
        var errorTask = DrainErrorsAsync(process.StandardError, cancellationToken);

        await SendRequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "limit_lens_dashboard",
                    title = "Limit Lens",
                    version = "0.1.1",
                },
            },
            cancellationToken).ConfigureAwait(false);
        await SendNotificationAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var tickTask = Task.Delay(
                TimeSpan.FromSeconds(Math.Clamp(settings.AccountRefreshSeconds, 30, 900)),
                cancellationToken);
            var completed = await Task.WhenAny(tickTask, outputTask).ConfigureAwait(false);
            if (completed == outputTask)
            {
                await outputTask.ConfigureAwait(false);
                break;
            }

            await tickTask.ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        await errorTask.ConfigureAwait(false);
    }

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("A duplicate request identifier was generated.");
        }

        try
        {
            var request = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["id"] = requestId,
                ["params"] = parameters,
            };
            await WriteMessageAsync(request, cancellationToken).ConfigureAwait(false);
            return await completion.Task
                .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(requestId, out _);
        }
    }

    private Task SendNotificationAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken) =>
        WriteMessageAsync(
            new Dictionary<string, object?>
            {
                ["method"] = method,
                ["params"] = parameters,
            },
            cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var writer = input ?? throw new IOException("Codex app-server input is unavailable.");
        var line = JsonSerializer.Serialize(message, SerializerOptions);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) &&
                    pending.TryRemove(id, out var completion))
                {
                    if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        var message = root.TryGetProperty("error", out var error) &&
                                      error.TryGetProperty("message", out var errorMessage)
                            ? errorMessage.GetString()
                            : "Codex app-server returned an error.";
                        completion.TrySetException(new InvalidOperationException(message));
                    }

                    continue;
                }

                if (root.TryGetProperty("method", out var methodElement) &&
                    methodElement.ValueKind == JsonValueKind.String &&
                    root.TryGetProperty("params", out var notificationParameters))
                {
                    ApplyNotification(methodElement.GetString(), notificationParameters);
                }
            }
            catch (JsonException)
            {
                // Ignore non-protocol diagnostic lines without exposing their contents.
            }
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            // Stderr can include sensitive diagnostics. Drain it without retaining or displaying it.
        }
    }

    private void ApplyNotification(string? method, JsonElement parameters)
    {
        if (method == "account/rateLimits/updated" &&
            parameters.TryGetProperty("rateLimits", out var rateLimitElement))
        {
            AccountUsageSnapshot snapshot;
            lock (stateGate)
            {
                if (!accountAvailable)
                {
                    return;
                }

                var update = ParseRateLimit(rateLimitElement, null);
                if (rateLimits.TryGetValue(update.Id, out var existing))
                {
                    rateLimits[update.Id] = existing.MergeSparse(update);
                }
                else
                {
                    rateLimits[update.Id] = update;
                }

                snapshot = CreateSnapshotLocked();
            }

            SnapshotChanged?.Invoke(snapshot);
        }
    }

    private bool ApplyAccount(JsonElement result)
    {
        if (!result.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null)
        {
            ClearAccountState();
            SetHealth(new SourceHealth(
                SourceConnectionState.SignedOut,
                "Sign in to Codex to see account limits. Local analytics are available.",
                Health.LastSuccessfulUpdate));
            return false;
        }

        var accountType = ReadString(account, "type");
        if (accountType == "apiKey")
        {
            ClearAccountState();
            SetHealth(new SourceHealth(
                SourceConnectionState.LocalOnly,
                "API-key mode does not expose ChatGPT account limits.",
                Health.LastSuccessfulUpdate));
            return false;
        }

        if (accountType != "chatgpt")
        {
            ClearAccountState();
            SetHealth(new SourceHealth(
                SourceConnectionState.LocalOnly,
                "This Codex authentication mode does not expose account usage.",
                Health.LastSuccessfulUpdate));
            return false;
        }

        lock (stateGate)
        {
            accountAvailable = true;
            planType = ReadString(account, "planType");
        }

        return true;
    }

    private void ApplyUsage(JsonElement result)
    {
        lock (stateGate)
        {
            ReplaceExtensions(
                "usage.",
                CaptureUnknown(result, "summary", "dailyUsageBuckets"));
            if (result.TryGetProperty("summary", out var summary))
            {
                accountSummary = new AccountUsageSummary(
                    ReadInt64(summary, "lifetimeTokens"),
                    ReadInt64(summary, "peakDailyTokens"),
                    ReadInt64(summary, "currentStreakDays"),
                    ReadInt64(summary, "longestStreakDays"),
                    ReadInt64(summary, "longestRunningTurnSec"));
            }

            if (result.TryGetProperty("dailyUsageBuckets", out var buckets) &&
                buckets.ValueKind == JsonValueKind.Array)
            {
                dailyUsage = buckets.EnumerateArray()
                    .Select(bucket =>
                    {
                        var dateText = ReadString(bucket, "startDate");
                        return DateOnly.TryParse(
                            dateText,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var date)
                            ? new DailyTokenUsage(date, ReadInt64(bucket, "tokens") ?? 0)
                            : null;
                    })
                    .Where(bucket => bucket is not null)
                    .Cast<DailyTokenUsage>()
                    .OrderBy(bucket => bucket.Date)
                    .ToArray();
            }
        }
    }

    private void ApplyRateLimits(JsonElement result)
    {
        lock (stateGate)
        {
            ReplaceExtensions(
                "rateLimits.",
                CaptureUnknown(result, "rateLimitsByLimitId", "rateLimits", "rateLimitResetCredits"));
            rateLimits.Clear();
            if (result.TryGetProperty("rateLimitsByLimitId", out var byId) &&
                byId.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in byId.EnumerateObject())
                {
                    var bucket = ParseRateLimit(property.Value, property.Name);
                    rateLimits[bucket.Id] = bucket;
                }
            }

            if (rateLimits.Count == 0 && result.TryGetProperty("rateLimits", out var fallback))
            {
                var bucket = ParseRateLimit(fallback, "codex");
                rateLimits[bucket.Id] = bucket;
            }

            if (result.TryGetProperty("rateLimitResetCredits", out var credits) &&
                credits.ValueKind == JsonValueKind.Object)
            {
                resetCredits = new ResetCreditSummary((int)(ReadInt64(credits, "availableCount") ?? 0));
            }
            else
            {
                resetCredits = null;
            }
        }
    }

    private RateLimitBucket ParseRateLimit(JsonElement element, string? fallbackId)
    {
        var id = ReadString(element, "limitId") ?? fallbackId ?? "codex";
        return new RateLimitBucket(
            id,
            ReadString(element, "limitName"),
            ReadString(element, "planType") ?? planType,
            ParseWindow(element, "primary"),
            ParseWindow(element, "secondary"),
            ParseCredits(element),
            ParseSpendControl(element),
            ReadBoolean(element, "spendControlReached"),
            ReadString(element, "rateLimitReachedType"),
            CaptureUnknown(
                element,
                "limitId",
                "limitName",
                "planType",
                "primary",
                "secondary",
                "credits",
                "individualLimit",
                "spendControlReached",
                "rateLimitReachedType"));
    }

    private static RateLimitWindow? ParseWindow(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var window) ||
            window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new RateLimitWindow(
            (int)(ReadInt64(window, "usedPercent") ?? 0),
            ReadInt64(window, "windowDurationMins") ?? ReadInt64(window, "window_minutes"),
            ReadUnixTimestamp(window, "resetsAt") ?? ReadUnixTimestamp(window, "resets_at"));
    }

    private static CreditsSnapshot? ParseCredits(JsonElement element)
    {
        if (!element.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new CreditsSnapshot(
            ReadBoolean(credits, "hasCredits") ?? false,
            ReadBoolean(credits, "unlimited") ?? false,
            ReadString(credits, "balance"));
    }

    private static SpendControlSnapshot? ParseSpendControl(JsonElement element)
    {
        if (!element.TryGetProperty("individualLimit", out var limit) ||
            limit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var resetsAt = ReadUnixTimestamp(limit, "resetsAt");
        return resetsAt is null
            ? null
            : new SpendControlSnapshot(
                ReadString(limit, "limit") ?? string.Empty,
                ReadString(limit, "used") ?? string.Empty,
                (int)(ReadInt64(limit, "remainingPercent") ?? 0),
                resetsAt.Value);
    }

    private void PublishSnapshot()
    {
        AccountUsageSnapshot snapshot;
        lock (stateGate)
        {
            snapshot = CreateSnapshotLocked();
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    private AccountUsageSnapshot CreateSnapshotLocked()
    {
        Current = new AccountUsageSnapshot(
            planType,
            accountSummary,
            dailyUsage,
            rateLimits.Values.OrderBy(bucket => bucket.Name ?? bucket.Id, StringComparer.OrdinalIgnoreCase).ToArray(),
            resetCredits,
            DateTimeOffset.Now,
            new Dictionary<string, JsonElement>(extensions, StringComparer.Ordinal));
        return Current;
    }

    private void ClearAccountState()
    {
        lock (stateGate)
        {
            accountAvailable = false;
            planType = null;
            accountSummary = new AccountUsageSummary();
            dailyUsage = [];
            rateLimits.Clear();
            resetCredits = null;
            extensions.Clear();
        }
    }

    private void ReplaceExtensions(
        string prefix,
        IReadOnlyDictionary<string, JsonElement> replacement)
    {
        foreach (var key in extensions.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
        {
            extensions.Remove(key);
        }

        foreach (var (key, value) in replacement)
        {
            extensions[prefix + key] = value;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> CaptureUnknown(
        JsonElement element,
        params string[] knownProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>();
        }

        var known = knownProperties.ToHashSet(StringComparer.Ordinal);
        return element.EnumerateObject()
            .Where(property => !known.Contains(property.Name))
            .ToDictionary(
                property => property.Name,
                property => property.Value.Clone(),
                StringComparer.Ordinal);
    }

    private void SetHealth(SourceHealth health)
    {
        Health = health;
        HealthChanged?.Invoke(health);
    }

    private void TryStopProcess()
    {
        input = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var (_, completion) in pending)
        {
            completion.TrySetException(exception);
        }

        pending.Clear();
    }

    private void SignalReconnect()
    {
        if (reconnectSignal.CurrentCount == 0)
        {
            reconnectSignal.Release();
        }
    }

    private async Task WaitForRetryOrSignalAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        using var retry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, retry.Token);
        var signalTask = reconnectSignal.WaitAsync(retry.Token);
        var completed = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
        await completed.ConfigureAwait(false);
        retry.Cancel();
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt64(out var value)
            ? value
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static DateTimeOffset? ReadUnixTimestamp(JsonElement element, string propertyName) =>
        ReadInt64(element, propertyName) is { } value
            ? DateTimeOffset.FromUnixTimeSeconds(value)
            : null;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
