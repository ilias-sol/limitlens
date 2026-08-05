using LimitLens.Core.Settings;
using LimitLens.Indexing.Storage;

namespace LimitLens.Tests;

public sealed class SettingsStoreTests
{
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
            CardOrder = [DashboardCardIds.Today, DashboardCardIds.Today, "unknown"],
            CompactCards = [.. DashboardCardIds.DefaultOrder],
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(DashboardSettings.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(DashboardTheme.Light, loaded.Theme);
        Assert.Equal(30, loaded.AccountRefreshSeconds);
        Assert.Equal([0, 25], loaded.AlertThresholds);
        Assert.Equal(DashboardCardIds.DefaultOrder.Count, loaded.CardOrder.Count);
        Assert.Equal(4, loaded.CompactCards.Count);
    }
}
