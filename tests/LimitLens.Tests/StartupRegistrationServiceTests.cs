using LimitLens.App.Services;

namespace LimitLens.Tests;

public sealed class StartupRegistrationServiceTests
{
    [Fact]
    public void BuildCommandQuotesExecutableAndUsesStartupArgument()
    {
        var command = StartupRegistrationService.BuildCommand(@"C:\Program Files\Limit Lens\LimitLens.exe");

        Assert.Equal("\"C:\\Program Files\\Limit Lens\\LimitLens.exe\" --startup", command);
    }

    [Theory]
    [InlineData("\"C:\\Apps\\LimitLens.exe\" --startup", @"C:\Apps\LimitLens.exe", true)]
    [InlineData("  \"C:\\APPS\\LIMITLENS.EXE\" --startup  ", @"C:\Apps\LimitLens.exe", true)]
    [InlineData("\"C:\\Old\\LimitLens.exe\" --startup", @"C:\Apps\LimitLens.exe", false)]
    [InlineData("\"C:\\Apps\\LimitLens.exe\"", @"C:\Apps\LimitLens.exe", false)]
    public void IsCommandForExecutableRequiresCurrentExecutableAndStartupArgument(
        string command,
        string executablePath,
        bool expected)
    {
        Assert.Equal(expected, StartupRegistrationService.IsCommandForExecutable(command, executablePath));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReconcileAppliesSavedPreference(bool actualState, bool desiredState)
    {
        var service = new FakeStartupService(actualState);

        var result = StartupRegistrationReconciler.Reconcile(service, desiredState);

        Assert.Equal(desiredState, result);
        Assert.Equal([desiredState], service.RequestedStates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconcileDoesNotRewriteMatchingRegistration(bool state)
    {
        var service = new FakeStartupService(state);

        var result = StartupRegistrationReconciler.Reconcile(service, state);

        Assert.Equal(state, result);
        Assert.Empty(service.RequestedStates);
    }

    private sealed class FakeStartupService(bool initialState) : IStartupRegistrationService
    {
        private bool isEnabled = initialState;

        public List<bool> RequestedStates { get; } = [];
        public bool IsEnabled => isEnabled;

        public void SetEnabled(bool enabled)
        {
            RequestedStates.Add(enabled);
            isEnabled = enabled;
        }
    }
}
