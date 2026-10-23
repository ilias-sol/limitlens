using LimitLens.Indexing.Claude;
using LimitLens.Indexing.Storage;

namespace LimitLens.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Claude Code runs the status-line bridge on every render, so it returns before WPF loads
        // and without joining the single running instance.
        if (args.Length > 0 && string.Equals(args[0], ClaudeStatusLineBridge.Argument, StringComparison.Ordinal))
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            return ClaudeStatusLineBridge.Run(args, input, output, AppStoragePaths.Detect().ClaudeUsagePath);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
