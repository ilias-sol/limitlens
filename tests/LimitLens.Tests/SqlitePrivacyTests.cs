using System.IO;
using System.Text;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Indexing.Storage;
using Microsoft.Data.Sqlite;

namespace LimitLens.Tests;

public sealed class SqlitePrivacyTests
{
    [Fact]
    public async Task DatabaseStoresOnlySanitizedSourceNamesAndDeclaredAggregates()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await using var repository = new SqliteUsageRepository(paths);
        await repository.InitializeAsync();
        var secretRoot = @"C:\PrivateFixture\PrivateWorkspace\.codex\sessions\secret-session.jsonl";
        var turn = new TurnAggregate(
            "turn",
            "session",
            "salted-id",
            "ProjectPhoenix",
            "gpt-5.6",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            TaskOutcome.Completed,
            new TokenUsageBreakdown(10, 2, 0, 5, 1, 15),
            1000,
            200,
            new Dictionary<ToolCategory, int> { [ToolCategory.Patch] = 1 });
        await repository.ReplaceSessionAsync(new SessionAggregate(
            "session",
            "salted-id",
            "ProjectPhoenix",
            turn.StartedAt,
            turn.StartedAt,
            [turn],
            turn.Tokens,
            secretRoot));
        await repository.SaveCheckpointAsync(new FileCheckpoint(
            "SESSION.JSONL",
            secretRoot,
            42,
            42,
            DateTimeOffset.UtcNow,
            "session"));

        var loaded = Assert.Single(await repository.LoadSessionsAsync());
        Assert.Equal("secret-session.jsonl", loaded.SourcePath);
        var checkpoint = await repository.GetCheckpointAsync("SESSION.JSONL");
        Assert.NotNull(checkpoint);
        Assert.Equal("secret-session.jsonl", checkpoint.Path);

        SqliteConnection.ClearAllPools();
        var databaseText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(paths.DatabasePath));
        Assert.DoesNotContain("PrivateWorkspace", databaseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\PrivateFixture", databaseText, StringComparison.OrdinalIgnoreCase);

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('turns');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.DoesNotContain(columns, column => column.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("command", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("argument", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, column => column.Contains("content", StringComparison.OrdinalIgnoreCase));
    }
}
