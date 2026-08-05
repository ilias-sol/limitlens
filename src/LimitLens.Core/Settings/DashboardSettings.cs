namespace LimitLens.Core.Settings;

public enum DashboardTheme
{
    DarkGlass,
    System,
    Light,
}

public enum DashboardWindowMode
{
    Compact,
    Expanded,
}

public static class DashboardCardIds
{
    public const string Limits = "limits";
    public const string Today = "today";
    public const string AccountSummary = "account-summary";
    public const string CurrentTask = "current-task";
    public const string DeviceActivity = "device-activity";
    public const string TokenBreakdown = "token-breakdown";

    public static IReadOnlyList<string> DefaultOrder { get; } =
    [
        Limits,
        Today,
        CurrentTask,
        AccountSummary,
        DeviceActivity,
        TokenBreakdown,
    ];
}

public sealed class WindowPlacementSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? MonitorDeviceName { get; set; }
}

public sealed class UsageHistorySample
{
    public DateTimeOffset Timestamp { get; set; }
    public int RemainingPercent { get; set; }
    public DateTimeOffset ResetAt { get; set; }
}

public sealed class DashboardSettings
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public DashboardWindowMode WindowMode { get; set; } = DashboardWindowMode.Compact;
    public DashboardTheme Theme { get; set; } = DashboardTheme.DarkGlass;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool WidgetAlwaysOnTop { get; set; } = true;
    public bool AutoCollapseWidget { get; set; }
    public bool ShowCreditsInWidget { get; set; } = true;
    public double WidgetOpacity { get; set; } = 1;
    public List<UsageHistorySample> UsageHistory { get; set; } = [];
    public bool AlertsEnabled { get; set; } = true;
    public DateTimeOffset? AlertsMutedUntil { get; set; }
    public int[] AlertThresholds { get; set; } = [25, 10, 0];
    public List<string> AlertDedupeKeys { get; set; } = [];
    public int AccountRefreshSeconds { get; set; } = 60;
    public string? CodexExecutablePath { get; set; }
    public string? CodexHomePath { get; set; }
    public string PrivacySalt { get; set; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    public List<string> CardOrder { get; set; } = [.. DashboardCardIds.DefaultOrder];
    public List<string> VisibleCards { get; set; } = [.. DashboardCardIds.DefaultOrder];
    public List<string> CompactCards { get; set; } =
    [
        DashboardCardIds.Today,
        DashboardCardIds.CurrentTask,
        DashboardCardIds.Limits,
        DashboardCardIds.AccountSummary,
    ];
    public WindowPlacementSettings CompactPlacement { get; set; } = new();
    public WindowPlacementSettings ExpandedPlacement { get; set; } = new();
}
