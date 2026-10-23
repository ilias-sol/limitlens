using System.IO;
using System.Text;
using System.Text.Json;
using LimitLens.Indexing.Claude;

namespace LimitLens.Tests;

public sealed class ClaudeStatusLineBridgeTests
{
    private const string StatusLineInput = """
        {
          "session_id": "secret-session",
          "cwd": "C:/Users/someone/private-project",
          "model": { "display_name": "Opus" },
          "workspace": { "current_dir": "C:/Users/someone/private-project" },
          "rate_limits": {
            "five_hour": { "used_percentage": 23.5, "resets_at": 4102444800 },
            "seven_day": { "used_percentage": 41.2, "resets_at": 4102876800 }
          }
        }
        """;

    [Fact]
    public void KeepsOnlyTheRateLimitWindows()
    {
        using var folder = new TempFolder();
        var path = folder.GetPath("claude-usage.json");

        var output = Run([ClaudeStatusLineBridge.Argument], StatusLineInput, path);

        var stored = File.ReadAllText(path);
        Assert.DoesNotContain("secret-session", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("private-project", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Opus", stored, StringComparison.Ordinal);
        var capture = Assert.IsType<ClaudeUsageCapture>(ClaudeUsageFile.TryRead(path));
        Assert.Equal(23.5, capture.FiveHour?.UsedPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(4102876800), capture.SevenDay?.ResetsAt);
        Assert.Equal("Claude 5h 77% left · weekly 59% left", output);
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("""{ "model": { "display_name": "Opus" } }""")]
    [InlineData("""{ "rate_limits": { "five_hour": { "used_percentage": "high" } } }""")]
    [InlineData("not json")]
    public void LeavesTheStoredLimitsAloneWithoutRateLimits(string input)
    {
        using var folder = new TempFolder();
        var path = folder.GetPath("claude-usage.json");
        Run([ClaudeStatusLineBridge.Argument], StatusLineInput, path);
        var before = File.ReadAllText(path);

        var output = Run([ClaudeStatusLineBridge.Argument], input, path);

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void AnIdleSessionCannotRollUsageBackWithinAWindow()
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(4102444800);
        var newer = new ClaudeUsageCapture(DateTimeOffset.Now, new(60, reset), new(40, reset.AddDays(3)));
        var staleSameWindow = new ClaudeUsageCapture(DateTimeOffset.Now, new(20, reset.AddSeconds(30)), null);
        var nextWindow = new ClaudeUsageCapture(DateTimeOffset.Now, new(5, reset.AddHours(5)), null);

        var merged = staleSameWindow.MergeOver(newer);
        Assert.Equal(60, merged.FiveHour?.UsedPercentage);
        Assert.Equal(40, merged.SevenDay?.UsedPercentage);
        Assert.Equal(5, nextWindow.MergeOver(newer).FiveHour?.UsedPercentage);
        Assert.Equal(60, newer.MergeOver(nextWindow with { FiveHour = new(5, reset.AddHours(-5)) }).FiveHour?.UsedPercentage);
    }

    [Fact]
    public void ChainArgumentsRoundTripCommandsWithQuotesAndSpaces()
    {
        const string previous = """bash -c "echo 'hi there' | cat" """;
        var command = ClaudeCodeSettingsInstaller.BuildCommand(@"C:\Program Files\Limit Lens\LimitLens.exe", previous);

        Assert.StartsWith("\"C:/Program Files/Limit Lens/LimitLens.exe\" --claude-statusline --chain ", command, StringComparison.Ordinal);
        var arguments = command[(command.IndexOf("--claude-statusline", StringComparison.Ordinal))..].Split(' ');
        Assert.Equal(previous, ClaudeStatusLineBridge.TryGetChainedCommand(arguments));
        Assert.Null(ClaudeStatusLineBridge.TryGetChainedCommand(["--claude-statusline", "--chain", "%%%"]));
    }

    [Fact]
    public void ParsesTheDocumentedStatusLineShape()
    {
        var capture = ClaudeStatusLineBridge.TryParse(Encoding.UTF8.GetBytes(StatusLineInput), DateTimeOffset.UnixEpoch);
        Assert.NotNull(capture);
        Assert.Equal(DateTimeOffset.UnixEpoch, capture.CapturedAt);
        Assert.Equal(41.2, capture.SevenDay?.UsedPercentage);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(capture));
        Assert.Equal(3, serialized.RootElement.EnumerateObject().Count());
    }

    private static string Run(string[] args, string input, string path)
    {
        using var stdin = new MemoryStream(Encoding.UTF8.GetBytes(input));
        using var stdout = new MemoryStream();
        Assert.Equal(0, ClaudeStatusLineBridge.Run(args, stdin, stdout, path));
        return Encoding.UTF8.GetString(stdout.ToArray());
    }
}
