using LimitLens.Core.Models;
using LimitLens.Core.Services;
using LimitLens.Indexing.Claude;

namespace LimitLens.Tests;

public sealed class ClaudeStatusLineClientTests
{
    [Fact]
    public async Task ReportsMissingUntilClaudeCodeWritesLimits()
    {
        using var folder = new TempFolder();
        var path = folder.GetPath("claude-usage.json");
        await using var client = new ClaudeStatusLineClient(path);

        await client.StartAsync();
        Assert.Equal(SourceConnectionState.Missing, client.Health.State);
        Assert.Empty(client.Current.RateLimits);

        var now = DateTimeOffset.Now;
        ClaudeUsageFile.Write(path, new ClaudeUsageCapture(now, new(23.5, now.AddHours(2)), new(41.2, now.AddDays(3))));
        await client.RefreshAsync();

        Assert.Equal(SourceConnectionState.Connected, client.Health.State);
        var bucket = Assert.Single(client.Current.RateLimits);
        Assert.Equal(UsageProvider.Claude, bucket.Provider);
        Assert.Equal("claude", bucket.Id);
        Assert.Equal(now, bucket.UpdatedAt);
        Assert.Equal(new RateLimitWindow(24, AccountLimitSelector.FiveHourMinutes, now.AddHours(2)), bucket.Primary);
        Assert.Equal(new RateLimitWindow(41, AccountLimitSelector.WeeklyMinutes, now.AddDays(3)), bucket.Secondary);
    }

    [Fact]
    public void DropsWindowsThatHaveAlreadyReset()
    {
        var now = DateTimeOffset.Now;
        var bucket = ClaudeStatusLineClient.ToBucket(
            new ClaudeUsageCapture(now.AddHours(-6), new(90, now.AddMinutes(-1)), new(50, now.AddDays(2))),
            now);
        Assert.NotNull(bucket);
        Assert.Null(bucket.Primary);
        Assert.Equal(50, bucket.Secondary?.UsedPercent);
        Assert.Null(ClaudeStatusLineClient.ToBucket(new ClaudeUsageCapture(now, new(90, now.AddMinutes(-1)), null), now));
    }

    [Fact]
    public async Task CompositeKeepsCodexAccountDataAndAddsTheClaudeBucket()
    {
        var now = DateTimeOffset.Now;
        var codex = new StubClient(AccountUsageSnapshot.Empty with
        {
            PlanType = "plus",
            UpdatedAt = now,
            RateLimits = [new("codex", Primary: new(10, 300, now.AddHours(1)))],
        }, new SourceHealth(SourceConnectionState.SignedOut, "Signed out"));
        var claude = new StubClient(AccountUsageSnapshot.Empty with
        {
            UpdatedAt = now.AddMinutes(-5),
            RateLimits = [new("claude", Primary: new(20, 300, now.AddHours(2)), Provider: UsageProvider.Claude, UpdatedAt: now.AddMinutes(-5))],
        }, new SourceHealth(SourceConnectionState.Connected, "Connected"));
        await using var composite = new CompositeAccountClient(codex, claude);
        var raised = 0;
        composite.SnapshotChanged += _ => raised++;

        Assert.Equal("plus", composite.Current.PlanType);
        Assert.Equal(now, composite.Current.UpdatedAt);
        Assert.Equal(["codex", "claude"], composite.Current.RateLimits.Select(bucket => bucket.Id));
        Assert.Equal(SourceConnectionState.Connected, composite.Health.State);
        Assert.Equal(SourceConnectionState.SignedOut, composite.GetHealth(UsageProvider.Codex).State);
        claude.Raise();
        Assert.Equal(1, raised);
    }

    private sealed class StubClient(AccountUsageSnapshot current, SourceHealth health) : LimitLens.Core.Abstractions.IAccountUsageClient
    {
        public AccountUsageSnapshot Current { get; } = current;
        public SourceHealth Health { get; } = health;
        public event Action<AccountUsageSnapshot>? SnapshotChanged;
        public event Action<SourceHealth>? HealthChanged;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Raise()
        {
            SnapshotChanged?.Invoke(Current);
            HealthChanged?.Invoke(Health);
        }
    }
}
