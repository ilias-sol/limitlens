namespace LimitLens.Tests;

/// <summary>
/// The UI smoke test creates the process-wide WPF Application on its own STA thread, and the
/// dashboard view model marshals events to that application's dispatcher. Running these classes
/// in parallel would deliver view-model updates to the smoke test's thread after a test asserts.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    public const string Name = "WPF application";
}
