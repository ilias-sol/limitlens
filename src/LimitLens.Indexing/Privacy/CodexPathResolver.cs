using LimitLens.Core.Settings;

namespace LimitLens.Indexing.Privacy;

public sealed class CodexPathResolver(DashboardSettings settings)
{
    public string ResolveHome()
    {
        if (IsExistingDirectory(settings.CodexHomePath))
        {
            return Path.GetFullPath(settings.CodexHomePath!);
        }

        var configuredHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (IsExistingDirectory(configuredHome))
        {
            return Path.GetFullPath(configuredHome!);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
    }

    public string ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(settings.CodexExecutablePath))
        {
            return settings.CodexExecutablePath;
        }

        return "codex";
    }

    private static bool IsExistingDirectory(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Directory.Exists(value);
}
