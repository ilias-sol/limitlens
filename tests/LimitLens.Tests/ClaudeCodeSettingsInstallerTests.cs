using System.IO;
using System.Text.Json.Nodes;
using LimitLens.Indexing.Claude;

namespace LimitLens.Tests;

public sealed class ClaudeCodeSettingsInstallerTests
{
    private const string ExecutablePath = @"C:\Apps\Limit Lens\LimitLens.exe";

    [Fact]
    public void ConnectsWithoutAnExistingSettingsFileAndDisconnectsCleanly()
    {
        using var folder = new TempFolder();
        var installer = new ClaudeCodeSettingsInstaller(folder.Path);
        Assert.Equal(ClaudeConnectionState.NotConnected, installer.GetState(ExecutablePath));

        var previous = installer.Connect(ExecutablePath, null);

        Assert.Null(previous);
        Assert.Equal(ClaudeConnectionState.Connected, installer.GetState(ExecutablePath));
        var statusLine = Read(installer)["statusLine"]!;
        Assert.Equal("command", statusLine["type"]!.GetValue<string>());
        Assert.Equal("\"C:/Apps/Limit Lens/LimitLens.exe\" --claude-statusline", statusLine["command"]!.GetValue<string>());

        installer.Disconnect(previous);
        Assert.Null(Read(installer)["statusLine"]);
        Assert.Equal(ClaudeConnectionState.NotConnected, installer.GetState(ExecutablePath));
    }

    [Fact]
    public void ChainsAndRestoresTheExistingStatusLineAndPreservesOtherSettings()
    {
        using var folder = new TempFolder();
        var installer = new ClaudeCodeSettingsInstaller(folder.Path);
        File.WriteAllText(installer.SettingsPath, """
            {
              // Claude Code tolerates comments.
              "model": "opus",
              "permissions": { "allow": ["Bash(git status)"] },
              "statusLine": { "type": "command", "command": "~/.claude/statusline.sh", "padding": 2 },
            }
            """);

        var previous = installer.Connect(ExecutablePath, null);

        Assert.True(File.Exists(installer.SettingsPath + ClaudeCodeSettingsInstaller.BackupSuffix));
        var connected = Read(installer);
        Assert.Equal("opus", connected["model"]!.GetValue<string>());
        Assert.Equal("Bash(git status)", connected["permissions"]!["allow"]![0]!.GetValue<string>());
        Assert.Equal(2, connected["statusLine"]!["padding"]!.GetValue<int>());
        var command = connected["statusLine"]!["command"]!.GetValue<string>();
        Assert.Equal(
            "~/.claude/statusline.sh",
            ClaudeStatusLineBridge.TryGetChainedCommand(command.Split(' ')));

        // Reconnecting from a moved copy keeps the original chain and restore point.
        var moved = installer.Connect(@"D:\Portable\LimitLens.exe", previous);
        Assert.Equal(previous, moved);
        Assert.Equal(ClaudeConnectionState.ConnectedElsewhere, installer.GetState(ExecutablePath));
        Assert.Equal(
            "~/.claude/statusline.sh",
            ClaudeStatusLineBridge.TryGetChainedCommand(Read(installer)["statusLine"]!["command"]!.GetValue<string>().Split(' ')));

        installer.Disconnect(moved);
        var restored = Read(installer)["statusLine"]!;
        Assert.Equal("~/.claude/statusline.sh", restored["command"]!.GetValue<string>());
        Assert.Equal(2, restored["padding"]!.GetValue<int>());
        Assert.Equal("opus", Read(installer)["model"]!.GetValue<string>());
    }

    [Fact]
    public void DisconnectLeavesSomeoneElsesStatusLineAlone()
    {
        using var folder = new TempFolder();
        var installer = new ClaudeCodeSettingsInstaller(folder.Path);
        const string settings = """{ "statusLine": { "type": "command", "command": "my-status" } }""";
        File.WriteAllText(installer.SettingsPath, settings);

        installer.Disconnect("""{ "type": "command", "command": "old" }""");

        Assert.Equal(settings, File.ReadAllText(installer.SettingsPath));
    }

    private static JsonObject Read(ClaudeCodeSettingsInstaller installer) =>
        Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(installer.SettingsPath)));
}
