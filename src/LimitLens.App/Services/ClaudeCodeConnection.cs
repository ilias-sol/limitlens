using LimitLens.Indexing.Claude;

namespace LimitLens.App.Services;

/// <summary>Connects this Limit Lens executable to the Claude Code status line.</summary>
public sealed class ClaudeCodeConnection(
    ClaudeCodeSettingsInstaller installer,
    string? executablePath,
    Func<bool>? hasGitBash = null)
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(executablePath);

    // Claude Code runs status lines through Git Bash when it is installed and PowerShell otherwise.
    // PowerShell neither waits for nor pipes input to a windowed executable, so only Git Bash works.
    public bool HasGitBash => (hasGitBash ?? (() => ClaudeStatusLineBridge.FindGitBash() is not null))();

    public ClaudeConnectionState GetState() =>
        IsAvailable ? installer.GetState(executablePath!) : ClaudeConnectionState.NotConnected;

    public string? Connect(string? previousStatusLine)
    {
        if (!HasGitBash)
        {
            throw new InvalidOperationException(
                "Claude Code runs status lines through PowerShell on this PC. Install Git for Windows (Git Bash), then connect again.");
        }

        return installer.Connect(
            executablePath ?? throw new InvalidOperationException("The Limit Lens executable path is unknown."),
            previousStatusLine);
    }

    public void Disconnect(string? previousStatusLine) => installer.Disconnect(previousStatusLine);
}
