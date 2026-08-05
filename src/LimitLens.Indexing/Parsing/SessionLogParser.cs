using System.Globalization;
using System.Text.Json;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Indexing.Privacy;

namespace LimitLens.Indexing.Parsing;

public sealed record SessionParseResult(SessionAggregate Session, long CompletedOffset);

public sealed class SessionLogParser(string privacySalt)
{
    public async Task<SessionParseResult?> ParseAsync(
        string fullPath,
        string relativePath,
        SessionAggregate? existing,
        long startOffset,
        CancellationToken cancellationToken = default)
    {
        var builder = new MutableSession(existing, relativePath, privacySalt);
        var completedOffset = startOffset;
        var sawRecord = false;

        await foreach (var line in JsonlRecordReader.ReadCompletedLinesAsync(
            fullPath,
            startOffset,
            cancellationToken).ConfigureAwait(false))
        {
            completedOffset = line.EndOffset;
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line.Text);
                builder.Apply(document.RootElement);
                sawRecord = true;
            }
            catch (JsonException)
            {
                // A malformed record is isolated to one line. Unknown and future records are ignored.
            }
        }

        if (!sawRecord && existing is null)
        {
            return null;
        }

        return new SessionParseResult(builder.Build(fullPath), completedOffset);
    }

    private sealed class MutableSession
    {
        private readonly Dictionary<string, MutableTurn> turns;
        private readonly string privacySalt;
        private string sessionId;
        private ProjectIdentity project;
        private DateTimeOffset startedAt;
        private DateTimeOffset updatedAt;
        private TokenUsageBreakdown lastCumulative;
        private string? activeTurnId;
        private string? currentModel;

        public MutableSession(
            SessionAggregate? existing,
            string relativePath,
            string privacySalt)
        {
            this.privacySalt = privacySalt;
            sessionId = existing?.SessionId ?? Path.GetFileNameWithoutExtension(relativePath);
            project = existing is null
                ? ProjectIdentity.FromPath(null, privacySalt)
                : new ProjectIdentity(existing.ProjectId, existing.ProjectDisplayName);
            startedAt = existing?.StartedAt ?? DateTimeOffset.MinValue;
            updatedAt = existing?.UpdatedAt ?? DateTimeOffset.MinValue;
            lastCumulative = existing?.LastCumulativeTokens ?? TokenUsageBreakdown.Empty;
            turns = existing?.Turns.ToDictionary(
                turn => turn.TurnId,
                turn => new MutableTurn(turn),
                StringComparer.Ordinal)
                ?? new Dictionary<string, MutableTurn>(StringComparer.Ordinal);
            activeTurnId = turns.Values
                .Where(turn => turn.Outcome == TaskOutcome.InProgress)
                .OrderByDescending(turn => turn.StartedAt)
                .Select(turn => turn.TurnId)
                .FirstOrDefault();
            currentModel = activeTurnId is null ? null : turns[activeTurnId].Model;
        }

        public void Apply(JsonElement root)
        {
            var timestamp = ReadTimestamp(root, "timestamp");
            if (timestamp is not null)
            {
                updatedAt = Max(updatedAt, timestamp.Value);
            }

            var outerType = ReadString(root, "type");
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            switch (outerType)
            {
                case "session_meta":
                    ApplySessionMeta(payload);
                    return;
                case "turn_context":
                    ApplyTurnContext(payload, timestamp);
                    return;
                case "event_msg":
                    ApplyEvent(payload, timestamp);
                    return;
                case "response_item":
                    ApplyResponseItem(payload);
                    return;
            }
        }

        public SessionAggregate Build(string fullPath)
        {
            if (startedAt == DateTimeOffset.MinValue)
            {
                startedAt = File.GetCreationTimeUtc(fullPath);
            }

            if (updatedAt == DateTimeOffset.MinValue)
            {
                updatedAt = File.GetLastWriteTimeUtc(fullPath);
            }

            return new SessionAggregate(
                sessionId,
                project.Id,
                project.DisplayName,
                startedAt,
                updatedAt,
                turns.Values
                    .OrderBy(turn => turn.StartedAt)
                    .Select(turn => turn.Build(sessionId))
                    .ToArray(),
                lastCumulative,
                Path.GetFileName(fullPath));
        }

        private void ApplySessionMeta(JsonElement payload)
        {
            sessionId = ReadString(payload, "session_id")
                ?? ReadString(payload, "id")
                ?? sessionId;
            var metaTimestamp = ReadTimestamp(payload, "timestamp");
            if (metaTimestamp is not null)
            {
                startedAt = startedAt == DateTimeOffset.MinValue
                    ? metaTimestamp.Value
                    : Min(startedAt, metaTimestamp.Value);
                updatedAt = Max(updatedAt, metaTimestamp.Value);
            }

            var cwd = ReadString(payload, "cwd");
            if (!string.IsNullOrWhiteSpace(cwd))
            {
                project = ProjectIdentity.FromPath(cwd, privacySalt);
            }
        }

        private void ApplyTurnContext(JsonElement payload, DateTimeOffset? timestamp)
        {
            var turnId = ReadString(payload, "turn_id");
            if (string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            var turnProject = ProjectIdentity.FromPath(ReadString(payload, "cwd"), privacySalt);
            if (turnProject.Id == "unknown")
            {
                turnProject = project;
            }

            currentModel = ReadString(payload, "model") ?? currentModel;
            var turn = GetOrCreateTurn(turnId, timestamp ?? updatedAt, turnProject);
            turn.Model = currentModel;
            turn.Project = turnProject;
            activeTurnId = turnId;
        }

        private void ApplyEvent(JsonElement payload, DateTimeOffset? timestamp)
        {
            switch (ReadString(payload, "type"))
            {
                case "task_started":
                    ApplyTaskStarted(payload, timestamp);
                    break;
                case "task_complete":
                    ApplyTaskFinished(payload, TaskOutcome.Completed, timestamp);
                    break;
                case "turn_aborted":
                    ApplyTaskFinished(payload, TaskOutcome.Aborted, timestamp);
                    break;
                case "token_count":
                    ApplyTokenCount(payload, timestamp);
                    break;
            }
        }

        private void ApplyTaskStarted(JsonElement payload, DateTimeOffset? timestamp)
        {
            var turnId = ReadString(payload, "turn_id");
            if (string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            var start = ReadTimestamp(payload, "started_at") ?? timestamp ?? updatedAt;
            var turn = GetOrCreateTurn(turnId, start, project);
            turn.StartedAt = start;
            turn.Outcome = TaskOutcome.InProgress;
            turn.Model ??= currentModel;
            activeTurnId = turnId;
            startedAt = startedAt == DateTimeOffset.MinValue ? start : Min(startedAt, start);
        }

        private void ApplyTaskFinished(
            JsonElement payload,
            TaskOutcome outcome,
            DateTimeOffset? timestamp)
        {
            var turnId = ReadString(payload, "turn_id") ?? activeTurnId;
            if (string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            var start = ReadTimestamp(payload, "started_at") ?? timestamp ?? updatedAt;
            var turn = GetOrCreateTurn(turnId, start, project);
            turn.CompletedAt = ReadTimestamp(payload, "completed_at") ?? timestamp;
            turn.DurationMilliseconds = ReadInt64(payload, "duration_ms") ?? turn.DurationMilliseconds;
            turn.TimeToFirstTokenMilliseconds =
                ReadInt64(payload, "time_to_first_token_ms") ?? turn.TimeToFirstTokenMilliseconds;
            turn.Outcome = outcome;
            if (activeTurnId == turnId)
            {
                activeTurnId = null;
            }
        }

        private void ApplyTokenCount(JsonElement payload, DateTimeOffset? timestamp)
        {
            if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("total_token_usage", out var totalElement) ||
                totalElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var total = ReadTokens(totalElement);
            var delta = total.PositiveDeltaFrom(lastCumulative);
            lastCumulative = total;

            var turn = GetActiveTurn(timestamp);
            if (turn is not null)
            {
                turn.Tokens = turn.Tokens.Add(delta);
            }
        }

        private void ApplyResponseItem(JsonElement payload)
        {
            var itemType = ReadString(payload, "type");
            if (itemType is not ("custom_tool_call" or "function_call"))
            {
                return;
            }

            var turn = GetActiveTurn(null);
            if (turn is null)
            {
                return;
            }

            var callId = ReadString(payload, "call_id") ?? ReadString(payload, "id");
            if (!string.IsNullOrWhiteSpace(callId) && !turn.ToolCallIds.Add(callId))
            {
                return;
            }

            var category = CategorizeTool(ReadString(payload, "name"));
            turn.ToolCounts[category] = turn.ToolCounts.GetValueOrDefault(category) + 1;
        }

        private MutableTurn? GetActiveTurn(DateTimeOffset? timestamp)
        {
            if (activeTurnId is not null && turns.TryGetValue(activeTurnId, out var active))
            {
                return active;
            }

            var latest = turns.Values.OrderByDescending(turn => turn.StartedAt).FirstOrDefault();
            if (latest is not null)
            {
                return latest;
            }

            if (timestamp is null)
            {
                return null;
            }

            var syntheticId = $"unattributed-{timestamp.Value.ToUnixTimeMilliseconds()}";
            activeTurnId = syntheticId;
            return GetOrCreateTurn(syntheticId, timestamp.Value, project);
        }

        private MutableTurn GetOrCreateTurn(
            string turnId,
            DateTimeOffset timestamp,
            ProjectIdentity turnProject)
        {
            if (turns.TryGetValue(turnId, out var existing))
            {
                return existing;
            }

            var created = new MutableTurn(turnId, timestamp, turnProject, currentModel);
            turns.Add(turnId, created);
            return created;
        }

        private static TokenUsageBreakdown ReadTokens(JsonElement element) => new(
            ReadInt64(element, "input_tokens") ?? 0,
            ReadInt64(element, "cached_input_tokens") ?? 0,
            ReadInt64(element, "cache_write_input_tokens") ?? 0,
            ReadInt64(element, "output_tokens") ?? 0,
            ReadInt64(element, "reasoning_output_tokens") ?? 0,
            ReadInt64(element, "total_tokens") ?? 0);

        private static ToolCategory CategorizeTool(string? name)
        {
            var normalized = name?.ToLowerInvariant() ?? string.Empty;
            if (normalized.Contains("apply_patch", StringComparison.Ordinal) ||
                normalized.Contains("patch", StringComparison.Ordinal))
            {
                return ToolCategory.Patch;
            }

            if (normalized.Contains("web", StringComparison.Ordinal) ||
                normalized.Contains("search", StringComparison.Ordinal))
            {
                return ToolCategory.Web;
            }

            if (normalized.Contains("browser", StringComparison.Ordinal) ||
                normalized.Contains("computer", StringComparison.Ordinal))
            {
                return ToolCategory.Browser;
            }

            if (normalized.Contains("exec", StringComparison.Ordinal) ||
                normalized.Contains("shell", StringComparison.Ordinal) ||
                normalized.Contains("command", StringComparison.Ordinal))
            {
                return ToolCategory.Shell;
            }

            if (normalized.Contains("mcp", StringComparison.Ordinal) ||
                normalized.Contains("codex_apps", StringComparison.Ordinal) ||
                normalized.Contains("connector", StringComparison.Ordinal))
            {
                return ToolCategory.Connector;
            }

            if (normalized.Contains("image", StringComparison.Ordinal))
            {
                return ToolCategory.Image;
            }

            return ToolCategory.Other;
        }

        private static string? ReadString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static long? ReadInt64(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var integer))
            {
                return integer;
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number)
                ? checked((long)number)
                : null;
        }

        private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            if (property.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(
                    property.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var timestamp))
            {
                return timestamp;
            }

            return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var unixSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                : null;
        }

        private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
            left > right ? left : right;

        private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
            left < right ? left : right;
    }

    private sealed class MutableTurn
    {
        public MutableTurn(
            string turnId,
            DateTimeOffset startedAt,
            ProjectIdentity project,
            string? model)
        {
            TurnId = turnId;
            StartedAt = startedAt;
            Project = project;
            Model = model;
        }

        public MutableTurn(TurnAggregate turn)
        {
            TurnId = turn.TurnId;
            StartedAt = turn.StartedAt;
            CompletedAt = turn.CompletedAt;
            Project = new ProjectIdentity(turn.ProjectId, turn.ProjectDisplayName);
            Model = turn.Model;
            Outcome = turn.Outcome;
            Tokens = turn.Tokens;
            DurationMilliseconds = turn.DurationMilliseconds;
            TimeToFirstTokenMilliseconds = turn.TimeToFirstTokenMilliseconds;
            ToolCounts = new Dictionary<ToolCategory, int>(turn.ToolCounts);
        }

        public string TurnId { get; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public ProjectIdentity Project { get; set; }
        public string? Model { get; set; }
        public TaskOutcome Outcome { get; set; } = TaskOutcome.InProgress;
        public TokenUsageBreakdown Tokens { get; set; } = TokenUsageBreakdown.Empty;
        public long DurationMilliseconds { get; set; }
        public long TimeToFirstTokenMilliseconds { get; set; }
        public Dictionary<ToolCategory, int> ToolCounts { get; set; } = [];
        public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);

        public TurnAggregate Build(string sessionId) => new(
            TurnId,
            sessionId,
            Project.Id,
            Project.DisplayName,
            Model,
            StartedAt,
            CompletedAt,
            Outcome,
            Tokens,
            DurationMilliseconds,
            TimeToFirstTokenMilliseconds,
            new Dictionary<ToolCategory, int>(ToolCounts));
    }
}
