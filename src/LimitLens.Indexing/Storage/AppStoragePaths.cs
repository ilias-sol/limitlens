namespace LimitLens.Indexing.Storage;

public sealed record AppStoragePaths(
    string RootDirectory,
    string DatabasePath,
    string SettingsPath,
    bool IsPortable)
{
    public static AppStoragePaths Detect()
    {
        var processPath = Environment.ProcessPath;
        var executableDirectory = string.IsNullOrWhiteSpace(processPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;

        var portable = File.Exists(Path.Combine(executableDirectory, "portable.flag"));
        var root = portable
            ? Path.Combine(executableDirectory, "Data")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LimitLens");

        return new AppStoragePaths(
            root,
            Path.Combine(root, "usage.db"),
            Path.Combine(root, "settings.json"),
            portable);
    }

    public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
}
