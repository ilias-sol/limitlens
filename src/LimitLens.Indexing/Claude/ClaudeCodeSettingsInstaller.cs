using System.Text.Json;
using System.Text.Json.Nodes;

namespace LimitLens.Indexing.Claude;

public enum ClaudeConnectionState
{
    NotConnected,
    Connected,
    // Limit Lens is configured, but through a different executable (for example a moved portable copy).
    ConnectedElsewhere,
}

/// <summary>
/// Points the Claude Code status line at Limit Lens and back. Only the <c>statusLine</c> key is
/// changed; every other setting is preserved, and the file is backed up before each edit.
/// </summary>
public sealed class ClaudeCodeSettingsInstaller(string configDirectory)
{
    public const string BackupSuffix = ".limitlens-backup";
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string SettingsPath => Path.Combine(configDirectory, "settings.json");

    public static string DefaultConfigDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configured;
    }

    /// <summary>Git Bash removes unquoted backslashes, so the path uses forward slashes.</summary>
    public static string BuildCommand(string executablePath, string? chainedCommand)
    {
        var command = $"\"{executablePath.Replace('\\', '/')}\" {ClaudeStatusLineBridge.Argument}";
        return string.IsNullOrWhiteSpace(chainedCommand)
            ? command
            : $"{command} {ClaudeStatusLineBridge.ChainArgument} {ClaudeStatusLineBridge.EncodeChain(chainedCommand)}";
    }

    public ClaudeConnectionState GetState(string executablePath)
    {
        var command = ReadCurrentCommand(ReadRoot());
        if (command is null || !IsLimitLensCommand(command))
        {
            return ClaudeConnectionState.NotConnected;
        }

        return command.Contains(executablePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? ClaudeConnectionState.Connected
            : ClaudeConnectionState.ConnectedElsewhere;
    }

    /// <summary>
    /// Installs Limit Lens as the status line, running any previous status line after it.
    /// Returns the previous <c>statusLine</c> object as JSON so it can be restored later.
    /// </summary>
    public string? Connect(string executablePath, string? previousStatusLine)
    {
        var root = ReadRoot() ?? new JsonObject();
        var current = root["statusLine"] as JsonObject;
        var currentCommand = ReadCurrentCommand(root);
        string? chained;
        if (currentCommand is not null && IsLimitLensCommand(currentCommand))
        {
            // Reconnecting (for example after moving the app) keeps the original chain and backup.
            chained = ClaudeStatusLineBridge.TryGetChainedCommand(SplitArguments(currentCommand));
        }
        else
        {
            previousStatusLine = current?.ToJsonString();
            chained = currentCommand;
        }

        var replacement = current is null ? new JsonObject() : (JsonObject)current.DeepClone();
        replacement["type"] = "command";
        replacement["command"] = BuildCommand(executablePath, chained);
        root["statusLine"] = replacement;
        Write(root);
        return previousStatusLine;
    }

    public void Disconnect(string? previousStatusLine)
    {
        var root = ReadRoot();
        if (root is null || ReadCurrentCommand(root) is not { } command || !IsLimitLensCommand(command))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousStatusLine) && JsonNode.Parse(previousStatusLine) is JsonObject previous)
        {
            root["statusLine"] = previous;
        }
        else
        {
            root.Remove("statusLine");
        }

        Write(root);
    }

    private static bool IsLimitLensCommand(string command) =>
        command.Contains(ClaudeStatusLineBridge.Argument, StringComparison.Ordinal);

    private static string? ReadCurrentCommand(JsonObject? root) =>
        root?["statusLine"] is JsonObject statusLine &&
        statusLine["command"] is JsonValue value &&
        value.TryGetValue<string>(out var command)
            ? command
            : null;

    // Our own commands contain only a quoted path and space-free arguments.
    private static string[] SplitArguments(string command) =>
        command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private JsonObject? ReadRoot()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
            ?? throw new InvalidOperationException("Claude Code settings.json is not a JSON object.");
    }

    private void Write(JsonObject root)
    {
        Directory.CreateDirectory(configDirectory);
        if (File.Exists(SettingsPath))
        {
            File.Copy(SettingsPath, SettingsPath + BackupSuffix, overwrite: true);
        }

        var temporaryPath = Path.Combine(configDirectory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(WriteOptions));
            File.Move(temporaryPath, SettingsPath, true);
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
