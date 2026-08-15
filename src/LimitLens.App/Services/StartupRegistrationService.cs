using Microsoft.Win32;

namespace LimitLens.App.Services;

public interface IStartupRegistrationService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LimitLens";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var executable = GetExecutablePath();
            return key?.GetValue(ValueName) is string value &&
                IsCommandForExecutable(value, executable);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("The Windows startup registry key is unavailable.");
            key.SetValue(ValueName, BuildCommand(GetExecutablePath()), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static string BuildCommand(string executablePath) => $"\"{executablePath}\" --startup";

    internal static bool IsCommandForExecutable(string command, string executablePath) =>
        string.Equals(command.Trim(), BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase);

    private static string GetExecutablePath() => Environment.ProcessPath
        ?? throw new InvalidOperationException("The application executable path is unavailable.");
}

internal static class StartupRegistrationReconciler
{
    public static bool Reconcile(IStartupRegistrationService startupService, bool desiredState)
    {
        if (startupService.IsEnabled != desiredState)
        {
            startupService.SetEnabled(desiredState);
        }

        return startupService.IsEnabled;
    }
}
