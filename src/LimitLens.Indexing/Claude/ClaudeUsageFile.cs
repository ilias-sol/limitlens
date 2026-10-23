using System.Text.Json;

namespace LimitLens.Indexing.Claude;

public sealed record ClaudeUsageWindow(double UsedPercentage, DateTimeOffset ResetsAt);

/// <summary>
/// The only Claude data Limit Lens keeps: the two rate-limit windows from the Claude Code
/// status-line input and when they were captured.
/// </summary>
public sealed record ClaudeUsageCapture(
    DateTimeOffset CapturedAt,
    ClaudeUsageWindow? FiveHour,
    ClaudeUsageWindow? SevenDay)
{
    private static readonly TimeSpan SameWindowTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Several Claude Code sessions write the same file, and an idle session can re-render with
    /// older numbers. Usage only grows within a window, so keep the higher reading of a window and
    /// never replace a newer window with an older one.
    /// </summary>
    public ClaudeUsageCapture MergeOver(ClaudeUsageCapture? existing) => existing is null
        ? this
        : this with
        {
            FiveHour = MergeWindow(FiveHour, existing.FiveHour),
            SevenDay = MergeWindow(SevenDay, existing.SevenDay),
        };

    private static ClaudeUsageWindow? MergeWindow(ClaudeUsageWindow? incoming, ClaudeUsageWindow? existing)
    {
        if (incoming is null || existing is null)
        {
            return incoming ?? existing;
        }

        if ((incoming.ResetsAt - existing.ResetsAt).Duration() <= SameWindowTolerance)
        {
            return incoming.UsedPercentage >= existing.UsedPercentage ? incoming : existing;
        }

        return incoming.ResetsAt > existing.ResetsAt ? incoming : existing;
    }
}

public static class ClaudeUsageFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static ClaudeUsageCapture? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ClaudeUsageCapture>(stream, SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Write(string path, ClaudeUsageCapture capture)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"claude-usage.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(capture, SerializerOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
