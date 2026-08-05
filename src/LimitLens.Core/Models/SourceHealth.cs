namespace LimitLens.Core.Models;

public enum SourceConnectionState
{
    Starting,
    Connected,
    LocalOnly,
    SignedOut,
    Missing,
    Offline,
    Faulted,
    Stopped,
}

public sealed record SourceHealth(
    SourceConnectionState State,
    string Message,
    DateTimeOffset? LastSuccessfulUpdate = null,
    string? Detail = null)
{
    public static SourceHealth Starting(string message) =>
        new(SourceConnectionState.Starting, message);
}

public sealed record DashboardSnapshot(
    AccountUsageSnapshot Account,
    LocalUsageAggregate Local,
    SourceHealth AccountHealth,
    SourceHealth LocalHealth);
