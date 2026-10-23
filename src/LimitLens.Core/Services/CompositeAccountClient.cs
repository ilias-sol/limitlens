using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;

namespace LimitLens.Core.Services;

/// <summary>
/// Presents Codex account usage and Claude limits as one snapshot. Codex supplies the plan,
/// summary, daily usage, and timestamp; each source contributes only its own rate-limit buckets.
/// </summary>
public sealed class CompositeAccountClient : IAccountUsageClient, IProviderHealthSource
{
    private readonly IAccountUsageClient codex;
    private readonly IAccountUsageClient claude;
    private bool disposed;

    public CompositeAccountClient(IAccountUsageClient codex, IAccountUsageClient claude)
    {
        this.codex = codex;
        this.claude = claude;
        codex.SnapshotChanged += OnSnapshotChanged;
        claude.SnapshotChanged += OnSnapshotChanged;
        codex.HealthChanged += OnHealthChanged;
        claude.HealthChanged += OnHealthChanged;
    }

    public AccountUsageSnapshot Current => Merge(codex.Current, claude.Current);

    // Codex remains the primary account source; Claude speaks for the account only when Codex cannot.
    public SourceHealth Health => codex.Health.State == SourceConnectionState.Connected ||
                                  claude.Health.State != SourceConnectionState.Connected
        ? codex.Health
        : claude.Health;

    public event Action<AccountUsageSnapshot>? SnapshotChanged;
    public event Action<SourceHealth>? HealthChanged;

    public SourceHealth GetHealth(UsageProvider provider) =>
        provider == UsageProvider.Claude ? claude.Health : codex.Health;

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(codex.StartAsync(cancellationToken), claude.StartAsync(cancellationToken));

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(codex.RefreshAsync(cancellationToken), claude.RefreshAsync(cancellationToken));

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        codex.SnapshotChanged -= OnSnapshotChanged;
        claude.SnapshotChanged -= OnSnapshotChanged;
        codex.HealthChanged -= OnHealthChanged;
        claude.HealthChanged -= OnHealthChanged;
        await codex.DisposeAsync().ConfigureAwait(false);
        await claude.DisposeAsync().ConfigureAwait(false);
    }

    // UpdatedAt stays the Codex account time; Claude buckets carry their own capture time.
    public static AccountUsageSnapshot Merge(AccountUsageSnapshot codexSnapshot, AccountUsageSnapshot claudeSnapshot) =>
        codexSnapshot with
        {
            RateLimits =
            [
                .. codexSnapshot.RateLimits.Where(bucket => bucket.Provider == UsageProvider.Codex),
                .. claudeSnapshot.RateLimits.Where(bucket => bucket.Provider == UsageProvider.Claude),
            ],
        };

    private void OnSnapshotChanged(AccountUsageSnapshot _) => SnapshotChanged?.Invoke(Current);

    private void OnHealthChanged(SourceHealth _) => HealthChanged?.Invoke(Health);
}
