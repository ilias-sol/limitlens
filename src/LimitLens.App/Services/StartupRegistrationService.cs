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
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
