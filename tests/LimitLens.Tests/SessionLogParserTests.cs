using System.IO;
using System.Text;
using System.Text.Json;
using LimitLens.Core.Models;
using LimitLens.Indexing.Parsing;

namespace LimitLens.Tests;

public sealed class SessionLogParserTests
{
    private const string Salt = "00112233445566778899AABBCCDDEEFF";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    [Fact]
    public async Task ParsesKnownRecordsWithoutRetainingSensitiveContent()
    {
        using var folder = new TempFolder();
        var path = folder.GetPath("rollout-session-1.jsonl");
        var sensitivePrompt = "NEVER_STORE_THIS_PRIVATE_PROMPT";
        var lines = new[]
        {
            "{\"timestamp\":\"2026-08-04T08:00:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"session-1\",\"timestamp\":\"2026-08-04T08:00:00Z\",\"cwd\":\"C:\\\\Private\\\\ProjectPhoenix\"}}",
            "{\"timestamp\":\"2026-08-04T08:00:01Z\",\"type\":\"turn_context\",\"payload\":{\"turn_id\":\"turn-1\",\"cwd\":\"C:\\\\Private\\\\ProjectPhoenix\",\"model\":\"gpt-5.6\"}}",
            "{\"timestamp\":\"2026-08-04T08:00:02Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\"}}",
            $"{{\"timestamp\":\"2026-08-04T08:00:03Z\",\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"content\":\"{sensitivePrompt}\"}}}}",
            "{\"timestamp\":\"2026-08-04T08:00:04Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"exec_command\",\"arguments\":\"PRIVATE_COMMAND_ARGUMENT\"}}",
            "{\"timestamp\":\"2026-08-04T08:00:04Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"call_id\":\"call-1\",\"name\":\"exec_command\",\"arguments\":\"PRIVATE_COMMAND_ARGUMENT\"}}",
            TokenLine("2026-08-04T08:00:05Z", 100, 40, 10, 30, 4, 140),
            TokenLine("2026-08-04T08:00:06Z", 160, 70, 12, 45, 7, 220),
            "{\"timestamp\":\"2026-08-04T08:00:08Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn-1\",\"duration_ms\":6000,\"time_to_first_token_ms\":900}}",
            "{malformed",
            "{\"timestamp\":\"2026-08-04T08:00:09Z\",\"type\":\"future_record\",\"payload\":{\"secret\":\"ignored\"}}",
        };
        await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", Utf8NoBom);

        var result = await new SessionLogParser(Salt).ParseAsync(path, "sessions/rollout-session-1.jsonl", null, 0);

        Assert.NotNull(result);
        Assert.Equal("session-1", result.Session.SessionId);
        Assert.Equal("ProjectPhoenix", result.Session.ProjectDisplayName);
        Assert.DoesNotContain("Private", result.Session.ProjectId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("rollout-session-1.jsonl", result.Session.SourcePath);
        var turn = Assert.Single(result.Session.Turns);
        Assert.Equal("gpt-5.6", turn.Model);
        Assert.Equal(TaskOutcome.Completed, turn.Outcome);
        Assert.Equal(160, turn.Tokens.InputTokens);
        Assert.Equal(220, turn.Tokens.TotalTokens);
        Assert.Equal(1, turn.ToolCounts[ToolCategory.Shell]);
        Assert.DoesNotContain(sensitivePrompt, result.Session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_COMMAND_ARGUMENT", result.Session.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoresPartialLineUntilItIsCompleted()
    {
        using var folder = new TempFolder();
        var path = folder.GetPath("partial.jsonl");
        var complete = "{\"timestamp\":\"2026-08-04T08:00:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"partial-session\",\"cwd\":\"C:\\\\Work\\\\SafeProject\"}}\n" +
                       "{\"timestamp\":\"2026-08-04T08:00:01Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn-1\"}}\n";
        var partial = TokenLine("2026-08-04T08:00:02Z", 12, 0, 0, 4, 1, 16);
        await File.WriteAllTextAsync(path, complete + partial, Utf8NoBom);
        var parser = new SessionLogParser(Salt);

        var first = await parser.ParseAsync(path, "sessions/partial.jsonl", null, 0);

        Assert.NotNull(first);
        Assert.Equal(0, Assert.Single(first.Session.Turns).Tokens.TotalTokens);
        Assert.Equal(Utf8NoBom.GetByteCount(complete), first.CompletedOffset);

        await File.AppendAllTextAsync(path, "\n", Utf8NoBom);
        var second = await parser.ParseAsync(path, "sessions/partial.jsonl", first.Session, first.CompletedOffset);

        Assert.NotNull(second);
        Assert.Equal(16, Assert.Single(second.Session.Turns).Tokens.TotalTokens);
    }

    private static string TokenLine(
        string timestamp,
        long input,
        long cached,
        long cacheWrite,
        long output,
        long reasoning,
        long total) => JsonSerializer.Serialize(new
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
                        input_tokens = input,
                        cached_input_tokens = cached,
                        cache_write_input_tokens = cacheWrite,
                        output_tokens = output,
                        reasoning_output_tokens = reasoning,
                        total_tokens = total,
                    },
                },
            },
        });
}
