using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LimitLens.Indexing.Claude;

/// <summary>
/// Runs as the Claude Code status-line command. It records the rate-limit windows from the
/// status-line JSON and prints a status line, either its own or the user's previous one.
/// </summary>
public static class ClaudeStatusLineBridge
{
    public const string Argument = "--claude-statusline";
    public const string ChainArgument = "--chain";
    private const int MaxInputBytes = 1024 * 1024;
    private static readonly TimeSpan ChainedTimeout = TimeSpan.FromSeconds(10);

    public static int Run(string[] args, Stream input, Stream output, string usagePath)
    {
        var payload = ReadInput(input);
        var capture = TryParse(payload, DateTimeOffset.Now);
        if (capture is not null)
        {
            try
            {
                ClaudeUsageFile.Write(usagePath, capture.MergeOver(ClaudeUsageFile.TryRead(usagePath)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The status line must keep rendering even when the data folder is unavailable.
            }
        }

        if (TryGetChainedCommand(args) is { } chained)
        {
            RunChained(chained, payload, output);
        }
        else if (capture is not null)
        {
            var line = Encoding.UTF8.GetBytes(DefaultLine(capture));
            output.Write(line);
        }

        output.Flush();
        return 0;
    }

    public static ClaudeUsageCapture? TryParse(ReadOnlySpan<byte> payload, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(payload.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("rate_limits", out var limits) ||
                limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fiveHour = ParseWindow(limits, "five_hour");
            var sevenDay = ParseWindow(limits, "seven_day");
            return fiveHour is null && sevenDay is null ? null : new ClaudeUsageCapture(now, fiveHour, sevenDay);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string DefaultLine(ClaudeUsageCapture capture)
    {
        var parts = new List<string>();
        if (capture.FiveHour is { } fiveHour)
        {
            parts.Add($"5h {Remaining(fiveHour)}% left");
        }

        if (capture.SevenDay is { } sevenDay)
        {
            parts.Add($"weekly {Remaining(sevenDay)}% left");
        }

        return $"Claude {string.Join(" · ", parts)}";
    }

    public static string EncodeChain(string command) => Convert.ToBase64String(Encoding.UTF8.GetBytes(command));

    public static string? TryGetChainedCommand(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!string.Equals(args[index], ChainArgument, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var command = Encoding.UTF8.GetString(Convert.FromBase64String(args[index + 1]));
                return string.IsNullOrWhiteSpace(command) ? null : command;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Claude Code on Windows runs status lines through Git Bash when it is installed, otherwise PowerShell.</summary>
    public static string? FindGitBash()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(directory => File.Exists(Path.Combine(directory, "git.exe")))
            .SelectMany(directory => new[]
            {
                Path.Combine(directory, "..", "bin", "bash.exe"),
                Path.Combine(directory, "bash.exe"),
            })
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"));
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static int Remaining(ClaudeUsageWindow window) =>
        (int)Math.Clamp(Math.Round(100 - window.UsedPercentage, MidpointRounding.AwayFromZero), 0, 100);

    private static ClaudeUsageWindow? ParseWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) ||
            window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var used) ||
            used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out var usedPercentage) ||
            !window.TryGetProperty("resets_at", out var resets) ||
            resets.ValueKind != JsonValueKind.Number ||
            !resets.TryGetInt64(out var resetsAt) ||
            double.IsNaN(usedPercentage))
        {
            return null;
        }

        try
        {
            return new ClaudeUsageWindow(Math.Clamp(usedPercentage, 0, 100), DateTimeOffset.FromUnixTimeSeconds(resetsAt));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] ReadInput(Stream input)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxInputBytes)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void RunChained(string command, byte[] payload, Stream output)
    {
        var bash = FindGitBash();
        var startInfo = new ProcessStartInfo
        {
            FileName = bash ?? "powershell.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        if (bash is null)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
        }

        startInfo.ArgumentList.Add(command);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            process.StandardInput.BaseStream.Write(payload);
            process.StandardInput.Close();
            if (!process.WaitForExit(ChainedTimeout))
            {
                process.Kill(entireProcessTree: true);
            }

            copy.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            var message = Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"Limit Lens could not run the previous status line ({exception.GetType().Name})."));
            output.Write(message);
        }
    }
}
