using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Services;
using Microsoft.Data.Sqlite;

namespace LimitLens.Indexing.Storage;

public sealed class SqliteUsageRepository(AppStoragePaths paths) : IUsageRepository
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS sessions (
                    session_id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    project_name TEXT NOT NULL,
                    started_at INTEGER NOT NULL,
                    updated_at INTEGER NOT NULL,
                    source_path TEXT NOT NULL,
                    cumulative_input INTEGER NOT NULL,
                    cumulative_cached INTEGER NOT NULL,
                    cumulative_cache_write INTEGER NOT NULL,
                    cumulative_output INTEGER NOT NULL,
                    cumulative_reasoning INTEGER NOT NULL,
                    cumulative_total INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS turns (
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    project_name TEXT NOT NULL,
                    model TEXT NULL,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER NULL,
                    outcome INTEGER NOT NULL,
                    input_tokens INTEGER NOT NULL,
                    cached_input_tokens INTEGER NOT NULL,
                    cache_write_input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    reasoning_output_tokens INTEGER NOT NULL,
                    total_tokens INTEGER NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    ttft_ms INTEGER NOT NULL,
                    PRIMARY KEY (session_id, turn_id),
                    FOREIGN KEY (session_id) REFERENCES sessions(session_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS tool_counts (
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    category INTEGER NOT NULL,
                    count INTEGER NOT NULL,
                    PRIMARY KEY (session_id, turn_id, category),
                    FOREIGN KEY (session_id, turn_id)
                        REFERENCES turns(session_id, turn_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS checkpoints (
                    file_key TEXT PRIMARY KEY,
                    relative_path TEXT NOT NULL,
                    offset_bytes INTEGER NOT NULL,
                    length_bytes INTEGER NOT NULL,
                    last_write_at INTEGER NOT NULL,
                    session_id TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_turns_started_at ON turns(started_at);
                CREATE INDEX IF NOT EXISTS ix_turns_project ON turns(project_id);
                CREATE INDEX IF NOT EXISTS ix_turns_model ON turns(model);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReplaceSessionAsync(
        SessionAggregate session,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = connection.BeginTransaction();

            await UpsertSessionAsync(connection, transaction, session, cancellationToken).ConfigureAwait(false);

            await using (var deleteTurns = connection.CreateCommand())
            {
                deleteTurns.Transaction = transaction;
                deleteTurns.CommandText = "DELETE FROM turns WHERE session_id = $session_id;";
                deleteTurns.Parameters.AddWithValue("$session_id", session.SessionId);
                await deleteTurns.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (var turn in session.Turns)
            {
                await InsertTurnAsync(connection, transaction, turn, cancellationToken).ConfigureAwait(false);
                foreach (var (category, count) in turn.ToolCounts)
                {
                    await using var toolCommand = connection.CreateCommand();
                    toolCommand.Transaction = transaction;
                    toolCommand.CommandText = """
                        INSERT INTO tool_counts(session_id, turn_id, category, count)
                        VALUES($session_id, $turn_id, $category, $count);
                        """;
                    toolCommand.Parameters.AddWithValue("$session_id", session.SessionId);
                    toolCommand.Parameters.AddWithValue("$turn_id", turn.TurnId);
                    toolCommand.Parameters.AddWithValue("$category", (int)category);
                    toolCommand.Parameters.AddWithValue("$count", count);
                    await toolCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SessionAggregate>> LoadSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var sessions = new Dictionary<string, SessionRow>(StringComparer.Ordinal);

            await using (var sessionCommand = connection.CreateCommand())
            {
                sessionCommand.CommandText = """
                    SELECT session_id, project_id, project_name, started_at, updated_at, source_path,
                           cumulative_input, cumulative_cached, cumulative_cache_write,
                           cumulative_output, cumulative_reasoning, cumulative_total
                    FROM sessions;
                    """;
                await using var reader = await sessionCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = new SessionRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        FromUnixMilliseconds(reader.GetInt64(3)),
                        FromUnixMilliseconds(reader.GetInt64(4)),
                        reader.GetString(5),
                        ReadTokens(reader, 6));
                    sessions.Add(row.SessionId, row);
                }
            }

            await using (var turnCommand = connection.CreateCommand())
            {
                turnCommand.CommandText = """
                    SELECT session_id, turn_id, project_id, project_name, model,
                           started_at, completed_at, outcome,
                           input_tokens, cached_input_tokens, cache_write_input_tokens,
                           output_tokens, reasoning_output_tokens, total_tokens,
                           duration_ms, ttft_ms
                    FROM turns
                    ORDER BY started_at;
                    """;
                await using var reader = await turnCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var sessionId = reader.GetString(0);
                    if (!sessions.TryGetValue(sessionId, out var session))
                    {
                        continue;
                    }

                    session.Turns.Add(new MutableTurnRow(
                        reader.GetString(1),
                        sessionId,
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        FromUnixMilliseconds(reader.GetInt64(5)),
                        reader.IsDBNull(6) ? null : FromUnixMilliseconds(reader.GetInt64(6)),
                        (TaskOutcome)reader.GetInt32(7),
                        ReadTokens(reader, 8),
                        reader.GetInt64(14),
                        reader.GetInt64(15)));
                }
            }

            await using (var toolCommand = connection.CreateCommand())
            {
                toolCommand.CommandText = """
                    SELECT session_id, turn_id, category, count
                    FROM tool_counts;
                    """;
                await using var reader = await toolCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!sessions.TryGetValue(reader.GetString(0), out var session))
                    {
                        continue;
                    }

                    var turn = session.Turns.FirstOrDefault(item => item.TurnId == reader.GetString(1));
                    if (turn is not null)
                    {
                        turn.ToolCounts[(ToolCategory)reader.GetInt32(2)] = reader.GetInt32(3);
                    }
                }
            }

            return sessions.Values
                .Select(row => row.Build())
                .OrderBy(session => session.StartedAt)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<LocalUsageAggregate> LoadAggregateAsync(
        CancellationToken cancellationToken = default) =>
        UsageAggregator.Aggregate(await LoadSessionsAsync(cancellationToken).ConfigureAwait(false));

    public async Task<FileCheckpoint?> GetCheckpointAsync(
        string fileKey,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT file_key, relative_path, offset_bytes, length_bytes, last_write_at, session_id
                FROM checkpoints
                WHERE file_key = $file_key;
                """;
            command.Parameters.AddWithValue("$file_key", fileKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new FileCheckpoint(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    FromUnixMilliseconds(reader.GetInt64(4)),
                    reader.IsDBNull(5) ? null : reader.GetString(5))
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveCheckpointAsync(
        FileCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO checkpoints(
                    file_key, relative_path, offset_bytes, length_bytes, last_write_at, session_id)
                VALUES($file_key, $path, $offset, $length, $last_write, $session_id)
                ON CONFLICT(file_key) DO UPDATE SET
                    relative_path = excluded.relative_path,
                    offset_bytes = excluded.offset_bytes,
                    length_bytes = excluded.length_bytes,
                    last_write_at = excluded.last_write_at,
                    session_id = excluded.session_id;
                """;
            command.Parameters.AddWithValue("$file_key", checkpoint.FileKey);
            command.Parameters.AddWithValue("$path", Path.GetFileName(checkpoint.Path));
            command.Parameters.AddWithValue("$offset", checkpoint.Offset);
            command.Parameters.AddWithValue("$length", checkpoint.Length);
            command.Parameters.AddWithValue("$last_write", ToUnixMilliseconds(checkpoint.LastWriteTime));
            command.Parameters.AddWithValue("$session_id", (object?)checkpoint.SessionId ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM checkpoints;
                DELETE FROM tool_counts;
                DELETE FROM turns;
                DELETE FROM sessions;
                VACUUM;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task UpsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionAggregate session,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions(
                session_id, project_id, project_name, started_at, updated_at, source_path,
                cumulative_input, cumulative_cached, cumulative_cache_write,
                cumulative_output, cumulative_reasoning, cumulative_total)
            VALUES(
                $session_id, $project_id, $project_name, $started_at, $updated_at, $source_path,
                $input, $cached, $cache_write, $output, $reasoning, $total)
            ON CONFLICT(session_id) DO UPDATE SET
                project_id = excluded.project_id,
                project_name = excluded.project_name,
                started_at = excluded.started_at,
                updated_at = excluded.updated_at,
                source_path = excluded.source_path,
                cumulative_input = excluded.cumulative_input,
                cumulative_cached = excluded.cumulative_cached,
                cumulative_cache_write = excluded.cumulative_cache_write,
                cumulative_output = excluded.cumulative_output,
                cumulative_reasoning = excluded.cumulative_reasoning,
                cumulative_total = excluded.cumulative_total;
            """;
        command.Parameters.AddWithValue("$session_id", session.SessionId);
        command.Parameters.AddWithValue("$project_id", session.ProjectId);
        command.Parameters.AddWithValue("$project_name", session.ProjectDisplayName);
        command.Parameters.AddWithValue("$started_at", ToUnixMilliseconds(session.StartedAt));
        command.Parameters.AddWithValue("$updated_at", ToUnixMilliseconds(session.UpdatedAt));
        command.Parameters.AddWithValue("$source_path", Path.GetFileName(session.SourcePath));
        AddTokenParameters(command, session.LastCumulativeTokens);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTurnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TurnAggregate turn,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO turns(
                session_id, turn_id, project_id, project_name, model,
                started_at, completed_at, outcome,
                input_tokens, cached_input_tokens, cache_write_input_tokens,
                output_tokens, reasoning_output_tokens, total_tokens,
                duration_ms, ttft_ms)
            VALUES(
                $session_id, $turn_id, $project_id, $project_name, $model,
                $started_at, $completed_at, $outcome,
                $input, $cached, $cache_write, $output, $reasoning, $total,
                $duration, $ttft);
            """;
        command.Parameters.AddWithValue("$session_id", turn.SessionId);
        command.Parameters.AddWithValue("$turn_id", turn.TurnId);
        command.Parameters.AddWithValue("$project_id", turn.ProjectId);
        command.Parameters.AddWithValue("$project_name", turn.ProjectDisplayName);
        command.Parameters.AddWithValue("$model", (object?)turn.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$started_at", ToUnixMilliseconds(turn.StartedAt));
        command.Parameters.AddWithValue(
            "$completed_at",
            turn.CompletedAt is null ? DBNull.Value : ToUnixMilliseconds(turn.CompletedAt.Value));
        command.Parameters.AddWithValue("$outcome", (int)turn.Outcome);
        AddTokenParameters(command, turn.Tokens);
        command.Parameters.AddWithValue("$duration", turn.DurationMilliseconds);
        command.Parameters.AddWithValue("$ttft", turn.TimeToFirstTokenMilliseconds);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddTokenParameters(SqliteCommand command, TokenUsageBreakdown tokens)
    {
        command.Parameters.AddWithValue("$input", tokens.InputTokens);
        command.Parameters.AddWithValue("$cached", tokens.CachedInputTokens);
        command.Parameters.AddWithValue("$cache_write", tokens.CacheWriteInputTokens);
        command.Parameters.AddWithValue("$output", tokens.OutputTokens);
        command.Parameters.AddWithValue("$reasoning", tokens.ReasoningOutputTokens);
        command.Parameters.AddWithValue("$total", tokens.TotalTokens);
    }

    private static TokenUsageBreakdown ReadTokens(SqliteDataReader reader, int offset) => new(
        reader.GetInt64(offset),
        reader.GetInt64(offset + 1),
        reader.GetInt64(offset + 2),
        reader.GetInt64(offset + 3),
        reader.GetInt64(offset + 4),
        reader.GetInt64(offset + 5));

    private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();
    private static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    private sealed class SessionRow(
        string sessionId,
        string projectId,
        string projectName,
        DateTimeOffset startedAt,
        DateTimeOffset updatedAt,
        string sourcePath,
        TokenUsageBreakdown cumulative)
    {
        public string SessionId { get; } = sessionId;
        public List<MutableTurnRow> Turns { get; } = [];

        public SessionAggregate Build() => new(
            SessionId,
            projectId,
            projectName,
            startedAt,
            updatedAt,
            Turns.Select(turn => turn.Build()).ToArray(),
            cumulative,
            sourcePath);
    }

    private sealed class MutableTurnRow(
        string turnId,
        string sessionId,
        string projectId,
        string projectName,
        string? model,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        TaskOutcome outcome,
        TokenUsageBreakdown tokens,
        long duration,
        long ttft)
    {
        public string TurnId { get; } = turnId;
        public Dictionary<ToolCategory, int> ToolCounts { get; } = [];

        public TurnAggregate Build() => new(
            TurnId,
            sessionId,
            projectId,
            projectName,
            model,
            startedAt,
            completedAt,
            outcome,
            tokens,
            duration,
            ttft,
            new Dictionary<ToolCategory, int>(ToolCounts));
    }
}
