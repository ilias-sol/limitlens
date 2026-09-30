using System.IO;
using LimitLens.Core.Settings;
using LimitLens.Indexing.Privacy;
using LimitLens.Indexing.Storage;

namespace LimitLens.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task PersistsSeparateTaskbarColourChoices()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await new JsonSettingsStore(paths).SaveAsync(new DashboardSettings
        {
            TaskbarTextColorMode = TaskbarColorMode.Black,
            TaskbarBarColorMode = TaskbarColorMode.Custom,
            TaskbarCustomTextColor = "aabbcc",
            TaskbarCustomBarColor = "#12abef",
        });
        var loaded = await new JsonSettingsStore(paths).LoadAsync();
        Assert.Equal(TaskbarColorMode.Black, loaded.TaskbarTextColorMode);
        Assert.Equal(TaskbarColorMode.Custom, loaded.TaskbarBarColorMode);
        Assert.Equal("#AABBCC", loaded.TaskbarCustomTextColor);
        Assert.Equal("#12ABEF", loaded.TaskbarCustomBarColor);
    }

    [Fact]
    public async Task RepairsInvalidTaskbarColoursAndPreservesAutomaticDefaults()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await new JsonSettingsStore(paths).SaveAsync(new DashboardSettings
        {
            TaskbarTextColorMode = (TaskbarColorMode)999,
            TaskbarBarColorMode = (TaskbarColorMode)999,
            TaskbarCustomTextColor = null!,
            TaskbarCustomBarColor = "#INVALID",
        });
        var loaded = await new JsonSettingsStore(paths).LoadAsync();
        Assert.Equal(TaskbarColorMode.Automatic, loaded.TaskbarTextColorMode);
        Assert.Equal(TaskbarColorMode.Automatic, loaded.TaskbarBarColorMode);
        Assert.Equal("#FFFFFF", loaded.TaskbarCustomTextColor);
        Assert.Equal("#FFFFFF", loaded.TaskbarCustomBarColor);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(37, 37)]
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    [InlineData(250, 200)]
    public async Task SavesAndRestoresTaskbarPositionWithinItsRange(int requested, int expected)
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await new JsonSettingsStore(paths).SaveAsync(new DashboardSettings { TaskbarPositionPercent = requested });
        Assert.Equal(expected, (await new JsonSettingsStore(paths).LoadAsync()).TaskbarPositionPercent);
    }

    [Fact]
    public async Task ExistingSettingsWithoutPositionKeepWidgetBesideTray()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await File.WriteAllTextAsync(paths.SettingsPath, "{\"schemaVersion\":10}");
        Assert.Equal(100, (await new JsonSettingsStore(paths).LoadAsync()).TaskbarPositionPercent);
        var loaded = await new JsonSettingsStore(paths).LoadAsync();
        Assert.Equal(TaskbarColorMode.Automatic, loaded.TaskbarTextColorMode);
        Assert.Equal(TaskbarColorMode.Automatic, loaded.TaskbarBarColorMode);
    }

    [Fact]
    public async Task DropsAmbiguousLegacyHistoryBeforeSwitchingToWeeklyForecasts()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        var store = new JsonSettingsStore(paths);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(new DashboardSettings
        {
            SchemaVersion = 9,
            UsageHistory = [new() { Timestamp = now, ResetAt = now.AddHours(2), RemainingPercent = 60 }],
        });
        Assert.Empty((await store.LoadAsync()).UsageHistory);
    }

    [Fact]
    public async Task RoundTripsOnlyTheLimitIdentityAndDurationNeededToSeparateHistory()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        var store = new JsonSettingsStore(paths);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(new DashboardSettings
        {
            UsageHistory = [new() { Timestamp = now, ResetAt = now.AddDays(4), RemainingPercent = 60,
                LimitId = "codex", WindowDurationMinutes = 10_080 }],
        });
        var loaded = await store.LoadAsync();
        var sample = Assert.Single(loaded.UsageHistory);
        Assert.Equal("codex", sample.LimitId);
        Assert.Equal(10_080, sample.WindowDurationMinutes);
        Assert.Equal(60, sample.RemainingPercent);
    }

    [Fact]
    public async Task NewInstallUsesWidgetFriendlyDefaults()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        var store = new JsonSettingsStore(paths);

        var loaded = await store.LoadAsync();

        Assert.Equal(DashboardTheme.DarkGlass, loaded.Theme);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.ShowCreditsInWidget);
        Assert.True(loaded.AlertsEnabled);
        Assert.Equal(FlyoutPosition.Right, loaded.FlyoutPosition);
        Assert.Equal([0, 10, 25], loaded.AlertThresholds);
    }

    [Fact]
    public async Task NormalizesVersionRangesAndCardLists()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        var store = new JsonSettingsStore(paths);
        var settings = new DashboardSettings
        {
            SchemaVersion = 0,
            AccountRefreshSeconds = 1,
            AlertThresholds = [-10, 0, 25, 25, 101],
            FlyoutPosition = (FlyoutPosition)999,
            CardOrder = [DashboardCardIds.Today, DashboardCardIds.Today, "unknown"],
            CompactCards = [.. DashboardCardIds.DefaultOrder],
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(DashboardSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(DashboardTheme.Light, loaded.Theme);
        Assert.Equal(FlyoutPosition.Right, loaded.FlyoutPosition);
        Assert.Equal(30, loaded.AccountRefreshSeconds);
        Assert.Equal([0, 25], loaded.AlertThresholds);
        Assert.Equal(DashboardCardIds.DefaultOrder.Count, loaded.CardOrder.Count);
        Assert.Equal(4, loaded.CompactCards.Count);
    }

    [Fact]
    public async Task RepairsNullThresholdsAndMalformedPrivacySalt()
    {
        using var folder = new TempFolder();
        var paths = new AppStoragePaths(folder.Path, folder.GetPath("usage.db"), folder.GetPath("settings.json"), true);
        await File.WriteAllTextAsync(
            paths.SettingsPath,
            """
            {
              "schemaVersion": 9,
              "alertThresholds": null,
              "privacySalt": "not-valid-hex"
            }
            """);
        var store = new JsonSettingsStore(paths);

        var loaded = await store.LoadAsync();

        Assert.Equal([10], loaded.AlertThresholds);
        Assert.Matches("^[0-9A-F]{32}$", loaded.PrivacySalt);
        var identity = ProjectIdentity.FromPath(@"C:\Work\Project", loaded.PrivacySalt);
        Assert.NotEmpty(identity.Id);

        var reloaded = await new JsonSettingsStore(paths).LoadAsync();
        Assert.Equal(loaded.PrivacySalt, reloaded.PrivacySalt);
    }
}
