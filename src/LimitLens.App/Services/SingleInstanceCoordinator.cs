using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace LimitLens.App.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string BaseMutexName = @"Local\LimitLens.FloatingDashboard.v1";
    private const string BasePipeName = "LimitLens.FloatingDashboard.Activation.v1";
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource lifetime = new();
    private Task? listener;
    private bool disposed;

    public SingleInstanceCoordinator(string? instanceName = null)
    {
        var suffix = string.IsNullOrWhiteSpace(instanceName) ? string.Empty : $".{instanceName}";
        pipeName = BasePipeName + suffix;
        mutex = new Mutex(initiallyOwned: true, BaseMutexName + suffix, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    internal static string PortableInstanceName(string executableDirectory)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory))
            .ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"Portable.{Convert.ToHexString(digest.AsSpan(0, 8))}";
    }

    public void StartListening(Action activationRequested)
    {
        if (!IsPrimary || listener is not null)
        {
            return;
        }

        listener = Task.Run(() => ListenAsync(pipeName, activationRequested, lifetime.Token), CancellationToken.None);
    }

    public static async Task SignalPrimaryAsync(string? instanceName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                BasePipeName + (string.IsNullOrWhiteSpace(instanceName) ? string.Empty : $".{instanceName}"),
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync("activate".AsMemory(), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            // The primary process may still be starting. A second process should exit either way.
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();
        if (IsPrimary)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }

        mutex.Dispose();
        lifetime.Dispose();
    }

    private static async Task ListenAsync(string pipeName, Action activationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, "activate", StringComparison.Ordinal))
                {
                    activationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
