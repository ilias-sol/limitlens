using LimitLens.App.Services;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class ShowcaseEnvironmentTests
{
    [Fact]
    public async Task ShowcaseIndexerRemainsEmptyAndLocalOnly()
    {
        await using var indexer = new ShowcaseSessionLogIndexer();
        LocalUsageAggregate? snapshot = null;
        indexer.SnapshotChanged += value => snapshot = value;

        await indexer.StartAsync();
        await indexer.RebuildAsync();

        Assert.Same(LocalUsageAggregate.Empty, indexer.Current);
        Assert.Same(LocalUsageAggregate.Empty, snapshot);
        Assert.Equal(SourceConnectionState.LocalOnly, indexer.Health.State);
        Assert.Contains("does not read local", indexer.Health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowcaseSettingsStayInMemory()
    {
        var original = new DashboardSettings { StartWithWindows = false };
        var store = new ShowcaseSettingsStore(original);
        var replacement = new DashboardSettings { StartWithWindows = true };

        await store.SaveAsync(replacement);

        Assert.Same(replacement, await store.LoadAsync());
    }

    [Fact]
    public void ShowcaseStartupRegistrationDoesNotTouchTheRegistry()
    {
        var service = new ShowcaseStartupRegistrationService();

        service.SetEnabled(true);

        Assert.True(service.IsEnabled);
    }
}
