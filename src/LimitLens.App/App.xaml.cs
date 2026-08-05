using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using LimitLens.App.Services;
using LimitLens.App.Taskbar;
using LimitLens.App.ViewModels;
using LimitLens.Core.Abstractions;
using LimitLens.Indexing.AppServer;
using LimitLens.Indexing.Indexing;
using LimitLens.Indexing.Storage;
using WpfMessageBox = System.Windows.MessageBox;

namespace LimitLens.App;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource lifetime = new();
    private SingleInstanceCoordinator? singleInstance;
    private AppStoragePaths? storagePaths;
    private ISettingsStore? settingsStore;
    private IUsageRepository? repository;
    private ICodexAppServerClient? accountClient;
    private ISessionLogIndexer? sessionIndexer;
    private DashboardViewModel? viewModel;
    private MainWindow? dashboardWindow;
    private TaskbarWidgetService? taskbarWidget;
    private TrayIconService? trayIcon;
    private AppNotificationService? notifications;
    private UsageAlertService? alertService;
    private Task? shutdownTask;

    protected override async void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var showcaseMode = args.Args.Any(argument => string.Equals(argument, "--showcase", StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(Path.Combine(executableDirectory, "showcase.flag"));
        var instanceName = showcaseMode ? "Showcase" : null;
        singleInstance = new SingleInstanceCoordinator(instanceName);
        if (!singleInstance.IsPrimary)
        {
            await SingleInstanceCoordinator.SignalPrimaryAsync(instanceName);
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown(0);
            return;
        }

        try
        {
            storagePaths = AppStoragePaths.Detect();
            storagePaths.EnsureCreated();
            var isFirstRun = !File.Exists(storagePaths.SettingsPath);
            settingsStore = new JsonSettingsStore(storagePaths);
            var settings = await settingsStore.LoadAsync();
            var startupService = new StartupRegistrationService();
            if (isFirstRun && !showcaseMode)
            {
                try
                {
                    startupService.SetEnabled(true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
                {
                    // Keep the app usable when startup registration is unavailable.
                }
            }

            settings.StartWithWindows = startupService.IsEnabled;
            if (isFirstRun)
            {
                await settingsStore.SaveAsync(settings);
            }

            repository = new SqliteUsageRepository(storagePaths);
            sessionIndexer = new SessionLogIndexer(repository, settings);
            if (showcaseMode)
            {
                var showcaseClient = new ShowcaseAccountClient(DateTimeOffset.Now);
                settings.UsageHistory = [.. showcaseClient.UsageHistory];
                settings.ShowCreditsInWidget = true;
                accountClient = showcaseClient;
            }
            else
            {
                accountClient = new CodexAppServerClient(settings);
            }
            viewModel = new DashboardViewModel(
                accountClient,
                sessionIndexer,
                settingsStore,
                startupService,
                settings,
                storagePaths.RootDirectory);
            dashboardWindow = new MainWindow(viewModel);
            MainWindow = dashboardWindow;

            taskbarWidget = new TaskbarWidgetService(viewModel);
            taskbarWidget.Clicked += bounds => Dispatch(() => dashboardWindow.ShowDashboard(bounds));
            taskbarWidget.ContextRequested += bounds => Dispatch(() => dashboardWindow.ShowDashboard(bounds, "Settings"));

            trayIcon = new TrayIconService(
                () => Dispatch(() => dashboardWindow.ShowDashboard(taskbarWidget.ScreenBounds)),
                () => Dispatch(() => dashboardWindow.ShowDashboard(taskbarWidget.ScreenBounds, "Settings")),
                RequestShutdown,
                () => DashboardThemeService.UsesLightPalette(viewModel.SelectedTheme));
            notifications = new AppNotificationService(trayIcon);
            alertService = new UsageAlertService(accountClient, settingsStore, settings, notifications);
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            singleInstance.StartListening(() => Dispatch(() => dashboardWindow.ShowDashboard(taskbarWidget.ScreenBounds)));
            DashboardThemeService.Apply(settings.Theme);

            var startHidden = args.Args.Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase));
            if ((showcaseMode || string.Equals(Environment.GetEnvironmentVariable("LIMIT_LENS_QA"), "1", StringComparison.Ordinal)) && !startHidden)
            {
                dashboardWindow.ShowDashboard(taskbarWidget.ScreenBounds);
            }

            _ = StartSourcesAsync();
        }
        catch (Exception exception)
        {
            WpfMessageBox.Show(
                $"Limit Lens could not start.\n\n{exception.Message}",
                "Limit Lens",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await DisposeServicesAsync();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs args)
    {
        lifetime.Cancel();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        alertService?.Dispose();
        notifications?.Dispose();
        trayIcon?.Dispose();
        taskbarWidget?.Dispose();
        viewModel?.Dispose();
        singleInstance?.Dispose();
        lifetime.Dispose();
        base.OnExit(args);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs args)
    {
        lifetime.Cancel();
        dashboardWindow?.PermitClose();
        base.OnSessionEnding(args);
    }

    private async Task StartSourcesAsync()
    {
        if (sessionIndexer is null || accountClient is null)
        {
            return;
        }

        var localTask = StartLocalSourceAsync(sessionIndexer, lifetime.Token);
        var accountTask = StartAccountSourceAsync(accountClient, lifetime.Token);
        await Task.WhenAll(localTask, accountTask);
    }

    private async Task StartLocalSourceAsync(ISessionLogIndexer indexer, CancellationToken cancellationToken)
    {
        try
        {
            await indexer.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Dispatch(() => notifications?.Show(
                "Local analytics are unavailable",
                $"Limit Lens could not index the local session folder: {exception.Message}"));
        }
    }

    private async Task StartAccountSourceAsync(ICodexAppServerClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.StartAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException exception)
        {
            Dispatch(() => notifications?.Show("Account usage is unavailable", exception.Message));
        }
    }

    private void RequestShutdown()
    {
        Dispatch(() => shutdownTask ??= ShutdownCoreAsync());
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs args)
    {
        if (args.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Dispatch(() => _ = viewModel?.RefreshAccountAsync());
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args)
    {
        if (args.IsAvailable)
        {
            Dispatch(() => _ = viewModel?.RefreshAccountAsync());
        }
    }

    private async Task ShutdownCoreAsync()
    {
        lifetime.Cancel();
        dashboardWindow?.PermitClose();
        dashboardWindow?.Close();
        await DisposeServicesAsync();
        Shutdown(0);
    }

    private async Task DisposeServicesAsync()
    {
        alertService?.Dispose();
        alertService = null;
        notifications?.Dispose();
        notifications = null;
        trayIcon?.Dispose();
        trayIcon = null;
        taskbarWidget?.Dispose();
        taskbarWidget = null;
        viewModel?.Dispose();
        viewModel = null;

        if (accountClient is not null)
        {
            await accountClient.DisposeAsync();
            accountClient = null;
        }

        if (sessionIndexer is not null)
        {
            await sessionIndexer.DisposeAsync();
            sessionIndexer = null;
        }

        if (repository is not null)
        {
            await repository.DisposeAsync();
            repository = null;
        }

        singleInstance?.Dispose();
        singleInstance = null;
    }

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = Dispatcher.InvokeAsync(action);
        }
    }
}
