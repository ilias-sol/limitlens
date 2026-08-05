using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using LimitLens.App.Converters;
using LimitLens.App.Services;
using LimitLens.Core.Abstractions;
using LimitLens.Core.Models;
using LimitLens.Core.Settings;
using Brush = System.Windows.Media.Brush;

namespace LimitLens.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> CardNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DashboardCardIds.Limits] = "Usage limits",
            [DashboardCardIds.Today] = "Today's tokens",
            [DashboardCardIds.AccountSummary] = "Account summary",
            [DashboardCardIds.CurrentTask] = "Current task",
            [DashboardCardIds.DeviceActivity] = "This-device activity",
            [DashboardCardIds.TokenBreakdown] = "Token breakdown",
        };

    private readonly ICodexAppServerClient accountClient;
    private readonly ISessionLogIndexer sessionIndexer;
    private readonly ISettingsStore settingsStore;
    private readonly IStartupRegistrationService startupService;
    private readonly DashboardSettings settings;
    private AccountUsageSnapshot account;
    private LocalUsageAggregate local;
    private SourceHealth accountHealth;
    private SourceHealth localHealth;
    private string selectedPage = "Overview";
    private bool showWidgetSettings;
    private double backfillProgress;
    private bool isRefreshing;
    private string statusMessage = "Starting Limit Lens…";
    private bool disposed;

    public DashboardViewModel(
        ICodexAppServerClient accountClient,
        ISessionLogIndexer sessionIndexer,
        ISettingsStore settingsStore,
        IStartupRegistrationService startupService,
        DashboardSettings settings,
        string dataLocation)
    {
        this.accountClient = accountClient;
        this.sessionIndexer = sessionIndexer;
        this.settingsStore = settingsStore;
        this.startupService = startupService;
        this.settings = settings;
        DataLocation = dataLocation;
        account = accountClient.Current;
        local = sessionIndexer.Current;
        accountHealth = accountClient.Health;
        localHealth = sessionIndexer.Health;

        accountClient.SnapshotChanged += OnAccountSnapshotChanged;
        accountClient.HealthChanged += OnAccountHealthChanged;
        sessionIndexer.SnapshotChanged += OnLocalSnapshotChanged;
        sessionIndexer.HealthChanged += OnLocalHealthChanged;
        sessionIndexer.BackfillProgressChanged += OnBackfillProgressChanged;

        NavigateCommand = new RelayCommand(parameter => SelectedPage = parameter as string ?? "Overview");
        ToggleModeCommand = new RelayCommand(_ => IsCompact = !IsCompact);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAccountAsync(), onException: ReportCommandFailure);
        RebuildCommand = new AsyncRelayCommand(_ => RebuildLocalDataAsync(), onException: ReportCommandFailure);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteLocalDataAsync(), onException: ReportCommandFailure);

        foreach (var id in settings.CardOrder)
        {
            CardSettings.Add(new CardPreferenceViewModel(
                id,
                CardNames[id],
                settings.VisibleCards.Contains(id, StringComparer.Ordinal),
                settings.CompactCards.Contains(id, StringComparer.Ordinal),
                OnCardPreferenceChanged));
        }

        RefreshDerived();
    }

    public ObservableCollection<DashboardCardViewModel> OverviewCards { get; } = [];
    public ObservableCollection<DashboardCardViewModel> CompactCards { get; } = [];
    public ObservableCollection<CardPreferenceViewModel> CardSettings { get; } = [];
    public ObservableCollection<LimitRowViewModel> LimitRows { get; } = [];
    public ObservableCollection<DailyRowViewModel> DailyRows { get; } = [];
    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];
    public ObservableCollection<ModelRowViewModel> Models { get; } = [];
    public ObservableCollection<string> ToolRows { get; } = [];

    public IReadOnlyList<DashboardTheme> ThemeOptions { get; } = Enum.GetValues<DashboardTheme>();
    public IReadOnlyList<int> RefreshOptions { get; } = [30, 60, 120, 300, 900];
    public string DataLocation { get; }
    public ICommand NavigateCommand { get; }
    public ICommand ToggleModeCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand RebuildCommand { get; }
    public ICommand DeleteCommand { get; }

    public string SelectedPage
    {
        get => selectedPage;
        set => SetProperty(ref selectedPage, value);
    }

    public bool IsCompact
    {
        get => settings.WindowMode == DashboardWindowMode.Compact;
        set
        {
            var mode = value ? DashboardWindowMode.Compact : DashboardWindowMode.Expanded;
            if (settings.WindowMode == mode)
            {
                return;
            }

            settings.WindowMode = mode;
            if (value)
            {
                ShowWidgetSettings = false;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExpanded));
            WindowModeChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public bool IsExpanded => !IsCompact;

    public bool ShowWidgetSettings
    {
        get => showWidgetSettings;
        set
        {
            if (SetProperty(ref showWidgetSettings, value))
            {
                OnPropertyChanged(nameof(ShowWidgetGraph));
                WidgetBehaviorChanged?.Invoke();
            }
        }
    }

    public bool ShowWidgetGraph => !ShowWidgetSettings;

    public bool WidgetAlwaysOnTop
    {
        get => settings.WidgetAlwaysOnTop;
        set
        {
            if (settings.WidgetAlwaysOnTop == value) return;
            settings.WidgetAlwaysOnTop = value;
            OnPropertyChanged();
            WidgetBehaviorChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public bool AutoCollapseWidget
    {
        get => settings.AutoCollapseWidget;
        set
        {
            if (settings.AutoCollapseWidget == value) return;
            settings.AutoCollapseWidget = value;
            OnPropertyChanged();
            QueueSettingsSave();
        }
    }

    public bool ShowCreditsInWidget
    {
        get => settings.ShowCreditsInWidget;
        set
        {
            if (settings.ShowCreditsInWidget == value) return;
            settings.ShowCreditsInWidget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CreditsVisible));
            WidgetBehaviorChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public double WidgetOpacity
    {
        get => settings.WidgetOpacity;
        set
        {
            var normalized = Math.Clamp(value, 0.65, 1);
            if (Math.Abs(settings.WidgetOpacity - normalized) < 0.001) return;
            settings.WidgetOpacity = normalized;
            OnPropertyChanged();
            WidgetBehaviorChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public bool CloseToTray
    {
        get => settings.CloseToTray;
        set
        {
            if (settings.CloseToTray == value)
            {
                return;
            }

            settings.CloseToTray = value;
            OnPropertyChanged();
            QueueSettingsSave();
        }
    }

    public bool StartWithWindows
    {
        get => settings.StartWithWindows;
        set
        {
            if (settings.StartWithWindows == value)
            {
                return;
            }

            try
            {
                startupService.SetEnabled(value);
                settings.StartWithWindows = value;
                StatusMessage = value ? "Startup registration enabled." : "Startup registration disabled.";
                OnPropertyChanged();
                QueueSettingsSave();
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
            {
                StatusMessage = $"Could not change startup registration: {exception.Message}";
                OnPropertyChanged();
            }
        }
    }

    public bool AlertsEnabled
    {
        get => settings.AlertsEnabled;
        set
        {
            if (settings.AlertsEnabled == value)
            {
                return;
            }

            settings.AlertsEnabled = value;
            OnPropertyChanged();
            QueueSettingsSave();
        }
    }

    public bool AlertAt25
    {
        get => settings.AlertThresholds.Contains(25);
        set => SetAlertThreshold(25, value);
    }

    public bool AlertAt10
    {
        get => settings.AlertThresholds.Contains(10);
        set => SetAlertThreshold(10, value);
    }

    public bool AlertAt0
    {
        get => settings.AlertThresholds.Contains(0);
        set => SetAlertThreshold(0, value);
    }

    public bool AlertsMuted => settings.AlertsMutedUntil > DateTimeOffset.Now;

    public string AlertsMutedText => AlertsMuted && settings.AlertsMutedUntil is { } until
        ? $"Muted until {until.LocalDateTime:g}"
        : "Mute until the next reset";

    public DashboardTheme SelectedTheme
    {
        get => settings.Theme;
        set
        {
            if (settings.Theme == value)
            {
                return;
            }

            settings.Theme = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseLightTheme));
            OnPropertyChanged(nameof(UsageRemainingBrush));
            AppearanceChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public bool UseLightTheme
    {
        get => DashboardThemeService.UsesLightPalette(SelectedTheme);
        set => SelectedTheme = value ? DashboardTheme.Light : DashboardTheme.DarkGlass;
    }

    public int RefreshSeconds
    {
        get => settings.AccountRefreshSeconds;
        set
        {
            var normalized = Math.Clamp(value, 30, 900);
            if (settings.AccountRefreshSeconds == normalized)
            {
                return;
            }

            settings.AccountRefreshSeconds = normalized;
            OnPropertyChanged();
            RefreshIntervalChanged?.Invoke();
            QueueSettingsSave();
        }
    }

    public string CodexExecutablePath
    {
        get => settings.CodexExecutablePath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (settings.CodexExecutablePath == normalized)
            {
                return;
            }

            settings.CodexExecutablePath = normalized;
            OnPropertyChanged();
            QueueSettingsSave();
        }
    }

    public string CodexHomePath
    {
        get => settings.CodexHomePath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (settings.CodexHomePath == normalized)
            {
                return;
            }

            settings.CodexHomePath = normalized;
            OnPropertyChanged();
            QueueSettingsSave();
        }
    }

    public double BackfillProgress
    {
        get => backfillProgress;
        private set => SetProperty(ref backfillProgress, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string AccountStatus => accountHealth.Message;
    public string LocalStatus => localHealth.Message;
    public string AccountStatusShort => accountHealth.State == SourceConnectionState.Connected
        ? "ACCOUNT LIVE"
        : accountHealth.State.ToString().ToUpperInvariant();
    public string LocalStatusShort => localHealth.State == SourceConnectionState.Connected
        ? "DEVICE LIVE"
        : localHealth.State.ToString().ToUpperInvariant();
    public bool IsRefreshing
    {
        get => isRefreshing;
        private set => SetProperty(ref isRefreshing, value);
    }
    public string FreshnessText
    {
        get
        {
            var updated = account.UpdatedAt > DateTimeOffset.MinValue
                ? account.UpdatedAt
                : accountHealth.LastSuccessfulUpdate;
            if (updated is null)
            {
                return accountHealth.State switch
                {
                    SourceConnectionState.SignedOut => "Signed out",
                    SourceConnectionState.Missing => "Codex missing",
                    SourceConnectionState.LocalOnly => "Account unavailable",
                    SourceConnectionState.Offline or SourceConnectionState.Faulted => "Offline",
                    _ => "Waiting for data",
                };
            }

            var age = DateTimeOffset.Now - updated.Value;
            var ageText = age < TimeSpan.FromSeconds(45)
                ? "just now"
                : age < TimeSpan.FromHours(1)
                    ? $"{Math.Max(1, (int)age.TotalMinutes)}m ago"
                    : $"{Math.Max(1, (int)age.TotalHours)}h ago";
            if (accountHealth.State is SourceConnectionState.Offline or SourceConnectionState.Faulted)
            {
                return $"Offline · {ageText}";
            }

            var staleAfter = TimeSpan.FromSeconds(Math.Max(120, settings.AccountRefreshSeconds * 2));
            return age > staleAfter ? $"Stale · {ageText}" : $"Updated {ageText}";
        }
    }
    public string PlanText
    {
        get
        {
            var plan = account.RateLimits
                .Select(bucket => bucket.PlanType)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? account.PlanType;
            return plan?.Trim().ToLowerInvariant() switch
            {
                "plus" => "ChatGPT Plus",
                "pro" => "ChatGPT Pro 5x",
                "pro-20x" => "ChatGPT Pro 20x",
                "business" or "team" => "ChatGPT Business",
                "enterprise" => "ChatGPT Enterprise",
                "edu" => "ChatGPT Edu",
                null or "" => "Plan unavailable",
                _ => $"ChatGPT {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(plan)}",
            };
        }
    }
    public string PlanShortText => PlanText == "Plan unavailable"
        ? string.Empty
        : PlanText.Replace("ChatGPT ", string.Empty, StringComparison.Ordinal);
    public string TodayAccountTokens { get; private set; } = "—";
    public string TodayDeviceTokens { get; private set; } = "0";
    public string LifetimeTokens { get; private set; } = "—";
    public string DeviceTotalTokens { get; private set; } = "0";
    public string CurrentTaskTokens { get; private set; } = "No active task";
    public string CurrentTaskDetail { get; private set; } = "Waiting for a local Codex task";
    public string CurrentStreak { get; private set; } = "—";
    public string PeakDay { get; private set; } = "—";
    public string LongestStreak { get; private set; } = "—";
    public string LongestTurn { get; private set; } = "—";
    public string TaskCount { get; private set; } = "0";
    public string TaskOutcomeSummary { get; private set; } = "No completed tasks";
    public string CompletionRate { get; private set; } = "0%";
    public string AverageTtft { get; private set; } = "—";
    public string TotalDuration { get; private set; } = "0m";
    public string InputTokens { get; private set; } = "0";
    public string CachedTokens { get; private set; } = "0";
    public string CacheWriteTokens { get; private set; } = "0";
    public string OutputTokens { get; private set; } = "0";
    public string ReasoningTokens { get; private set; } = "0";
    public double[] TrendValues { get; private set; } = [];
    public double ForecastRemainingPercent { get; private set; } = 100;
    public Brush UsageRemainingBrush => UsageRemainingBrushConverter.Select(ForecastRemainingPercent, SelectedTheme);
    public double ForecastElapsedPercent { get; private set; }
    public double ForecastProjectedRemaining { get; private set; } = 100;
    public DateTimeOffset? ForecastWindowStart { get; private set; }
    public DateTimeOffset? ForecastResetAt { get; private set; }
    public DateTimeOffset? ForecastEmptyAt { get; private set; }
    public bool ForecastHasPrediction { get; private set; }
    public double ForecastUsagePointsPerDay { get; private set; }
    public string ForecastCurrentText { get; private set; } = "Usage forecast unavailable";
    public string ForecastSummary { get; private set; } = "Waiting for a complete rate-limit window.";
    public IReadOnlyList<UsageHistorySample> ForecastActualPoints => settings.UsageHistory;
    public string WidgetRemainingText => $"{ForecastRemainingPercent:0}% remaining";
    public string WidgetResetText => ForecastResetAt is { } reset ? FormatReset(reset) : "Reset time unavailable";
    public string WidgetResetShortText => ForecastResetAt is { } reset ? FormatRelativeReset(reset) : "Reset unavailable";
    public string ForecastStatusText => ForecastRemainingPercent <= 10
        ? "Critical"
        : ForecastEmptyAt is { } empty && ForecastResetAt is { } reset && empty < reset
            ? "At risk"
            : ForecastRemainingPercent <= 25 ? "Low" : "On track";
    public string ForecastInsightTitle => ForecastEmptyAt is not null ? "At this pace" : "Current outlook";
    public string ForecastResetMetric => ForecastResetAt is { } reset
        ? reset.ToLocalTime().ToString("MMM d · HH:mm", CultureInfo.CurrentCulture)
        : "Unavailable";
    public string ForecastPaceMetric
    {
        get
        {
            if (ForecastWindowStart is not { } start || ForecastResetAt is not { } reset || reset <= start)
            {
                return "Unavailable";
            }
            return ForecastHasPrediction
                ? $"{ForecastUsagePointsPerDay:0.#} pts/day"
                : "Collecting data";
        }
    }
    public string ForecastRunwayMetric => ForecastEmptyAt switch
    {
        { } empty when empty <= DateTimeOffset.Now => "At limit",
        { } empty => FormatDuration(empty - DateTimeOffset.Now),
        _ when ForecastResetAt is not null => "Through reset",
        _ => "Unavailable",
    };
    public string CreditsText { get; private set; } = string.Empty;
    public bool CreditsAvailable { get; private set; }
    public bool CreditsVisible => ShowCreditsInWidget && CreditsAvailable;
    public string ResetCreditsText => account.ResetCredits is { } resetCredits
        ? resetCredits.AvailableCount.ToString("N0", CultureInfo.CurrentCulture)
        : "—";

    public event Action? WindowModeChanged;
    public event Action? AppearanceChanged;
    public event Action? RefreshIntervalChanged;
    public event Action? WidgetBehaviorChanged;

    public DashboardSettings Settings => settings;

    public void Tick()
    {
        if (settings.AlertsMutedUntil <= DateTimeOffset.Now && settings.AlertsMutedUntil is not null)
        {
            settings.AlertsMutedUntil = null;
            OnPropertyChanged(nameof(AlertsMuted));
            OnPropertyChanged(nameof(AlertsMutedText));
            QueueSettingsSave();
        }

        if (local.CurrentTask is { } current)
        {
            CurrentTaskDetail = $"{current.ProjectDisplayName} · {current.Model ?? "Unknown model"} · {FormatDuration(DateTimeOffset.Now - current.StartedAt)}";
            OnPropertyChanged(nameof(CurrentTaskDetail));
        }

        RefreshForecast();
        RefreshCards();
        OnPropertyChanged(nameof(FreshnessText));
    }

    public void MuteUntilReset()
    {
        var reset = account.RateLimits
            .SelectMany(bucket => new[] { bucket.Primary?.ResetsAt, bucket.Secondary?.ResetsAt })
            .Where(value => value > DateTimeOffset.Now)
            .Max();
        settings.AlertsMutedUntil = reset ?? DateTimeOffset.Now.AddHours(1);
        OnPropertyChanged(nameof(AlertsMuted));
        OnPropertyChanged(nameof(AlertsMutedText));
        QueueSettingsSave();
    }

    public void MoveCard(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= CardSettings.Count || newIndex < 0 || newIndex >= CardSettings.Count || oldIndex == newIndex)
        {
            return;
        }

        CardSettings.Move(oldIndex, newIndex);
        settings.CardOrder = CardSettings.Select(card => card.Id).ToList();
        RefreshCards();
        QueueSettingsSave();
    }

    public void PersistWindowSettings() => QueueSettingsSave();

    public async Task RefreshAccountAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "Refreshing account usage…";
        try
        {
            await accountClient.RefreshAsync();
            StatusMessage = "Account refresh complete.";
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TimeoutException)
        {
            StatusMessage = $"Account refresh failed: {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
            OnPropertyChanged(nameof(FreshnessText));
        }
    }

    public async Task RebuildLocalDataAsync()
    {
        StatusMessage = "Rebuilding privacy-safe local analytics…";
        await sessionIndexer.RebuildAsync();
        StatusMessage = "Local analytics rebuilt.";
    }

    public async Task DeleteLocalDataAsync()
    {
        await sessionIndexer.DeleteAllAsync();
        StatusMessage = "Local analytics deleted. Live indexing is paused until Reindex is selected.";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        accountClient.SnapshotChanged -= OnAccountSnapshotChanged;
        accountClient.HealthChanged -= OnAccountHealthChanged;
        sessionIndexer.SnapshotChanged -= OnLocalSnapshotChanged;
        sessionIndexer.HealthChanged -= OnLocalHealthChanged;
        sessionIndexer.BackfillProgressChanged -= OnBackfillProgressChanged;
    }

    private void OnAccountSnapshotChanged(AccountUsageSnapshot snapshot) => RunOnUi(() =>
    {
        account = snapshot;
        RefreshDerived();
        OnPropertyChanged(nameof(FreshnessText));
    });

    private void OnAccountHealthChanged(SourceHealth health) => RunOnUi(() =>
    {
        accountHealth = health;
        StatusMessage = health.Message;
        NotifyStatusProperties();
        OnPropertyChanged(nameof(FreshnessText));
    });

    private void OnLocalSnapshotChanged(LocalUsageAggregate snapshot) => RunOnUi(() =>
    {
        local = snapshot;
        RefreshDerived();
    });

    private void OnLocalHealthChanged(SourceHealth health) => RunOnUi(() =>
    {
        localHealth = health;
        StatusMessage = health.Message;
        NotifyStatusProperties();
    });

    private void OnBackfillProgressChanged(double progress) => RunOnUi(() => BackfillProgress = progress * 100);

    private void RefreshDerived()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var accountToday = account.DailyUsage.FirstOrDefault(bucket => bucket.Date == today);
        var localToday = local.DailyUsage.FirstOrDefault(bucket => bucket.Date == today);
        TodayAccountTokens = accountToday is null ? "—" : FormatTokens(accountToday.Tokens);
        TodayDeviceTokens = FormatTokens(localToday?.Tokens.TotalTokens ?? 0);
        LifetimeTokens = account.Summary.LifetimeTokens is { } lifetime ? FormatTokens(lifetime) : "—";
        DeviceTotalTokens = FormatTokens(local.Tokens.TotalTokens);
        CurrentStreak = account.Summary.CurrentStreakDays is { } streak ? $"{streak} days" : "—";
        PeakDay = account.Summary.PeakDailyTokens is { } peak ? FormatTokens(peak) : "—";
        LongestStreak = account.Summary.LongestStreakDays is { } longestStreak ? $"{longestStreak} days" : "—";
        LongestTurn = account.Summary.LongestRunningTurnSeconds is { } longestTurn
            ? FormatDuration(TimeSpan.FromSeconds(longestTurn))
            : "—";
        TaskCount = local.Tasks.ToString("N0", CultureInfo.CurrentCulture);
        TaskOutcomeSummary = $"{local.CompletedTasks:N0} complete · {local.AbortedTasks:N0} aborted";
        CompletionRate = local.Tasks == 0 ? "—" : $"{local.CompletionRate:P0}";
        AverageTtft = local.AverageTimeToFirstTokenMilliseconds == 0
            ? "—"
            : FormatDuration(TimeSpan.FromMilliseconds(local.AverageTimeToFirstTokenMilliseconds));
        TotalDuration = FormatDuration(TimeSpan.FromMilliseconds(local.TotalDurationMilliseconds));
        InputTokens = FormatTokens(local.Tokens.InputTokens);
        CachedTokens = FormatTokens(local.Tokens.CachedInputTokens);
        CacheWriteTokens = FormatTokens(local.Tokens.CacheWriteInputTokens);
        OutputTokens = FormatTokens(local.Tokens.OutputTokens);
        ReasoningTokens = FormatTokens(local.Tokens.ReasoningOutputTokens);

        var credits = account.RateLimits.Select(bucket => bucket.Credits).FirstOrDefault(value => value?.HasCredits == true);
        CreditsAvailable = credits is not null && (credits.Unlimited || !string.IsNullOrWhiteSpace(credits.Balance));
        CreditsText = credits switch
        {
            { Unlimited: true } => "Unlimited",
            { Balance: { Length: > 0 } balance } => FormatCreditBalance(balance),
            _ => string.Empty,
        };

        if (local.CurrentTask is { } current)
        {
            CurrentTaskTokens = FormatTokens(current.Tokens.TotalTokens);
            CurrentTaskDetail = $"{current.ProjectDisplayName} · {current.Model ?? "Unknown model"} · {FormatDuration(DateTimeOffset.Now - current.StartedAt)}";
        }
        else
        {
            CurrentTaskTokens = "No active task";
            CurrentTaskDetail = "Waiting for a local Codex task";
        }

        var accountTrend = account.DailyUsage.OrderBy(bucket => bucket.Date).TakeLast(14).Select(bucket => (double)bucket.Tokens).ToArray();
        TrendValues = accountTrend.Length > 1
            ? accountTrend
            : local.DailyUsage.OrderBy(bucket => bucket.Date).TakeLast(14).Select(bucket => (double)bucket.Tokens.TotalTokens).ToArray();

        RefreshForecast();
        RefreshRows();
        RefreshCards();
        NotifyMetricProperties();
    }

    private void RefreshRows()
    {
        ReplaceCollection(LimitRows, account.RateLimits.Select(bucket => new LimitRowViewModel(
            bucket.Id,
            bucket.Name ?? bucket.Id,
            bucket.PlanType ?? account.PlanType ?? "Unknown plan",
            bucket.Primary?.UsedPercent ?? 0,
            bucket.Primary is null ? "Unavailable" : $"{bucket.Primary.UsedPercent}% used",
            FormatReset(bucket.Primary?.ResetsAt))));

        ReplaceCollection(DailyRows, local.DailyUsage
            .OrderByDescending(day => day.Date)
            .Take(30)
            .Select(day => new DailyRowViewModel(
                day.Date.ToString("ddd, MMM d", CultureInfo.CurrentCulture),
                day.Tokens.TotalTokens,
                FormatTokens(day.Tokens.TotalTokens),
                day.Tasks,
                day.Tasks == 0 ? "—" : $"{(double)day.CompletedTasks / day.Tasks:P0}",
                FormatDuration(TimeSpan.FromMilliseconds(day.DurationMilliseconds)))));

        var projectMaximum = Math.Max(1, local.Projects.FirstOrDefault()?.Tokens.TotalTokens ?? 0);
        ReplaceCollection(Projects, local.Projects.Select(project => new ProjectRowViewModel(
            project.DisplayName,
            FormatTokens(project.Tokens.TotalTokens),
            project.Tasks,
            FormatDuration(TimeSpan.FromMilliseconds(project.DurationMilliseconds)),
            project.Tokens.TotalTokens * 100d / projectMaximum)));

        var modelMaximum = Math.Max(1, local.Models.FirstOrDefault()?.Tokens.TotalTokens ?? 0);
        ReplaceCollection(Models, local.Models.Select(model => new ModelRowViewModel(
            model.Model,
            FormatTokens(model.Tokens.TotalTokens),
            model.Tasks,
            model.Tokens.TotalTokens * 100d / modelMaximum)));

        ReplaceCollection(ToolRows, local.ToolCategories.Select(tool => $"{tool.Category}: {tool.Count:N0}"));
    }

    private void RefreshCards()
    {
        var preferredBucket = account.RateLimits.FirstOrDefault(bucket => bucket.Primary is not null);
        var primary = preferredBucket?.Primary;
        var secondary = preferredBucket?.Secondary;
        var limitDetail = primary is null
            ? accountHealth.Message
            : secondary is null
                ? FormatReset(primary.ResetsAt)
                : $"{FormatReset(primary.ResetsAt)} · secondary {secondary.UsedPercent}%";
        var accountSummaryDetail = $"{PlanText} · streak {CurrentStreak} (best {LongestStreak}) · peak {PeakDay} · longest turn {LongestTurn}";
        var cards = new Dictionary<string, DashboardCardViewModel>(StringComparer.Ordinal)
        {
            [DashboardCardIds.Limits] = new(
                DashboardCardIds.Limits,
                "PRIMARY LIMIT",
                primary is null ? "Unavailable" : $"{primary.UsedPercent}% used",
                limitDetail,
                primary?.UsedPercent ?? 0,
                primary is not null,
                true),
            [DashboardCardIds.Today] = new(
                DashboardCardIds.Today,
                "TODAY · ACCOUNT / DEVICE",
                $"{TodayAccountTokens}  /  {TodayDeviceTokens}",
                "Account totals and this-device totals are kept separate",
                0,
                false,
                false),
            [DashboardCardIds.CurrentTask] = new(
                DashboardCardIds.CurrentTask,
                "CURRENT TASK · THIS DEVICE",
                CurrentTaskTokens,
                CurrentTaskDetail,
                0,
                false,
                false),
            [DashboardCardIds.AccountSummary] = new(
                DashboardCardIds.AccountSummary,
                "ACCOUNT LIFETIME",
                LifetimeTokens,
                accountSummaryDetail,
                0,
                false,
                false),
            [DashboardCardIds.DeviceActivity] = new(
                DashboardCardIds.DeviceActivity,
                "THIS DEVICE",
                DeviceTotalTokens,
                $"{local.Tasks:N0} tasks · {CompletionRate} complete",
                0,
                false,
                false),
            [DashboardCardIds.TokenBreakdown] = new(
                DashboardCardIds.TokenBreakdown,
                "LOCAL TOKEN MIX",
                $"{InputTokens} in · {OutputTokens} out",
                $"{CachedTokens} cached · {ReasoningTokens} reasoning",
                0,
                false,
                false),
        };

        var orderedVisible = settings.CardOrder
            .Where(id => settings.VisibleCards.Contains(id, StringComparer.Ordinal))
            .Select(id => cards[id]);
        ReplaceCollection(OverviewCards, orderedVisible);
        ReplaceCollection(CompactCards, settings.CardOrder
            .Where(id => settings.VisibleCards.Contains(id, StringComparer.Ordinal) &&
                         settings.CompactCards.Contains(id, StringComparer.Ordinal))
            .Select(id => cards[id]));
    }

    private void OnCardPreferenceChanged(CardPreferenceViewModel changed)
    {
        if (changed.IsCompact && CardSettings.Count(card => card.IsCompact) > 4)
        {
            changed.SetCompactSilently(false);
            StatusMessage = "The compact panel supports up to four cards.";
        }

        settings.VisibleCards = CardSettings.Where(card => card.IsVisible).Select(card => card.Id).ToList();
        settings.CompactCards = CardSettings.Where(card => card.IsCompact).Select(card => card.Id).ToList();
        RefreshCards();
        QueueSettingsSave();
    }

    private void SetAlertThreshold(int threshold, bool enabled)
    {
        var values = settings.AlertThresholds.ToHashSet();
        if (enabled)
        {
            values.Add(threshold);
        }
        else
        {
            values.Remove(threshold);
        }

        settings.AlertThresholds = values.Order().ToArray();
        OnPropertyChanged(threshold switch
        {
            25 => nameof(AlertAt25),
            10 => nameof(AlertAt10),
            _ => nameof(AlertAt0),
        });
        QueueSettingsSave();
    }

    private void QueueSettingsSave() => _ = SaveSettingsSafeAsync();

    private void ReportCommandFailure(Exception exception) =>
        StatusMessage = $"The operation failed: {exception.Message}";

    private async Task SaveSettingsSafeAsync()
    {
        try
        {
            await settingsStore.SaveAsync(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunOnUi(() => StatusMessage = $"Settings could not be saved: {exception.Message}");
        }
    }

    private void NotifyMetricProperties()
    {
        OnPropertyChanged(nameof(TodayAccountTokens));
        OnPropertyChanged(nameof(TodayDeviceTokens));
        OnPropertyChanged(nameof(LifetimeTokens));
        OnPropertyChanged(nameof(DeviceTotalTokens));
        OnPropertyChanged(nameof(CurrentTaskTokens));
        OnPropertyChanged(nameof(CurrentTaskDetail));
        OnPropertyChanged(nameof(CurrentStreak));
        OnPropertyChanged(nameof(PeakDay));
        OnPropertyChanged(nameof(LongestStreak));
        OnPropertyChanged(nameof(LongestTurn));
        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(TaskOutcomeSummary));
        OnPropertyChanged(nameof(CompletionRate));
        OnPropertyChanged(nameof(AverageTtft));
        OnPropertyChanged(nameof(TotalDuration));
        OnPropertyChanged(nameof(InputTokens));
        OnPropertyChanged(nameof(CachedTokens));
        OnPropertyChanged(nameof(CacheWriteTokens));
        OnPropertyChanged(nameof(OutputTokens));
        OnPropertyChanged(nameof(ReasoningTokens));
        OnPropertyChanged(nameof(TrendValues));
        OnPropertyChanged(nameof(PlanText));
        OnPropertyChanged(nameof(PlanShortText));
        OnPropertyChanged(nameof(CreditsText));
        OnPropertyChanged(nameof(CreditsAvailable));
        OnPropertyChanged(nameof(CreditsVisible));
        OnPropertyChanged(nameof(ResetCreditsText));
        WidgetBehaviorChanged?.Invoke();
        NotifyStatusProperties();
    }

    private void RefreshForecast()
    {
        var bucket = account.RateLimits.FirstOrDefault(candidate => candidate.Primary is not null);
        var window = bucket?.Primary;
        ForecastResetAt = window?.ResetsAt;
        ForecastEmptyAt = null;
        ForecastHasPrediction = false;
        ForecastUsagePointsPerDay = 0;

        if (window?.ResetsAt is not { } reset || window.WindowDurationMinutes is not > 0)
        {
            ForecastWindowStart = null;
            ForecastRemainingPercent = window?.RemainingPercent ?? 100;
            ForecastElapsedPercent = 0;
            ForecastProjectedRemaining = ForecastRemainingPercent;
            ForecastCurrentText = window is null ? "Usage forecast unavailable" : $"{window.RemainingPercent}% remaining";
            ForecastSummary = "A reset time and window duration are required for a projection.";
            NotifyForecastProperties();
            return;
        }

        var duration = TimeSpan.FromMinutes(window.WindowDurationMinutes.Value);
        var start = reset - duration;
        var now = DateTimeOffset.Now;
        var elapsedFraction = duration.TotalSeconds <= 0
            ? 0
            : Math.Clamp((now - start).TotalSeconds, 0, duration.TotalSeconds) / duration.TotalSeconds;
        ForecastWindowStart = start;
        ForecastRemainingPercent = window.RemainingPercent;
        ForecastElapsedPercent = elapsedFraction * 100;
        ForecastProjectedRemaining = window.RemainingPercent;
        ForecastCurrentText = $"{window.RemainingPercent}% remaining · resets {FormatForecastDate(reset)}";
        RecordUsageSample(window, reset, now);

        if (TryCalculateRecentForecast(start, reset, now, out var slopePerSecond))
        {
            ForecastHasPrediction = true;
            ForecastUsagePointsPerDay = -slopePerSecond * TimeSpan.FromDays(1).TotalSeconds;
            ForecastProjectedRemaining = Math.Clamp(
                window.RemainingPercent + slopePerSecond * Math.Max(0, (reset - now).TotalSeconds),
                0,
                100);
            var empty = now.AddSeconds(window.RemainingPercent / -slopePerSecond);
            if (empty < reset)
            {
                ForecastEmptyAt = empty;
                ForecastSummary = empty > now
                    ? $"At the current pace, usage reaches 0% {FormatForecastDate(empty)}."
                    : "The current pace has already crossed the sustainable reset rate.";
            }
            else
            {
                ForecastSummary = $"At the recent pace, about {ForecastProjectedRemaining:0}% remains at reset.";
            }
        }
        else
        {
            ForecastSummary = "Collecting usage history for a reliable prediction.";
        }

        NotifyForecastProperties();
    }

    private void NotifyForecastProperties()
    {
        OnPropertyChanged(nameof(ForecastRemainingPercent));
        OnPropertyChanged(nameof(UsageRemainingBrush));
        OnPropertyChanged(nameof(ForecastElapsedPercent));
        OnPropertyChanged(nameof(ForecastProjectedRemaining));
        OnPropertyChanged(nameof(ForecastWindowStart));
        OnPropertyChanged(nameof(ForecastResetAt));
        OnPropertyChanged(nameof(ForecastEmptyAt));
        OnPropertyChanged(nameof(ForecastHasPrediction));
        OnPropertyChanged(nameof(ForecastUsagePointsPerDay));
        OnPropertyChanged(nameof(ForecastCurrentText));
        OnPropertyChanged(nameof(ForecastSummary));
        OnPropertyChanged(nameof(ForecastActualPoints));
        OnPropertyChanged(nameof(WidgetRemainingText));
        OnPropertyChanged(nameof(WidgetResetText));
        OnPropertyChanged(nameof(WidgetResetShortText));
        OnPropertyChanged(nameof(ForecastStatusText));
        OnPropertyChanged(nameof(ForecastInsightTitle));
        OnPropertyChanged(nameof(ForecastResetMetric));
        OnPropertyChanged(nameof(ForecastPaceMetric));
        OnPropertyChanged(nameof(ForecastRunwayMetric));
    }

    private void RecordUsageSample(RateLimitWindow window, DateTimeOffset reset, DateTimeOffset now)
    {
        var timestamp = account.UpdatedAt > DateTimeOffset.MinValue ? account.UpdatedAt : now;
        timestamp = timestamp > now.AddMinutes(1) ? now : timestamp;
        settings.UsageHistory.RemoveAll(sample => sample.ResetAt != reset || sample.Timestamp < ForecastWindowStart);

        var last = settings.UsageHistory.LastOrDefault();
        if (last is not null &&
            last.Timestamp == timestamp &&
            last.RemainingPercent == window.RemainingPercent)
        {
            return;
        }

        if (last is not null && timestamp <= last.Timestamp)
        {
            return;
        }

        settings.UsageHistory.Add(new UsageHistorySample
        {
            Timestamp = timestamp,
            RemainingPercent = window.RemainingPercent,
            ResetAt = reset,
        });
        if (settings.UsageHistory.Count > 512)
        {
            settings.UsageHistory.RemoveRange(0, settings.UsageHistory.Count - 512);
        }

        QueueSettingsSave();
    }

    private bool TryCalculateRecentForecast(
        DateTimeOffset start,
        DateTimeOffset reset,
        DateTimeOffset now,
        out double slopePerSecond)
    {
        slopePerSecond = 0;
        var cutoff = now - TimeSpan.FromHours(24);
        if (cutoff < start)
        {
            cutoff = start;
        }

        var samples = settings.UsageHistory
            .Where(sample => sample.ResetAt == reset && sample.Timestamp >= cutoff && sample.Timestamp <= now.AddMinutes(1))
            .OrderBy(sample => sample.Timestamp)
            .GroupBy(sample => sample.Timestamp)
            .Select(group => group.Last())
            .ToArray();
        if (samples.Length < 3 || samples[^1].Timestamp - samples[0].Timestamp < TimeSpan.FromMinutes(10))
        {
            return false;
        }

        if (samples[0].RemainingPercent - samples[^1].RemainingPercent < 1)
        {
            return false;
        }

        var origin = samples[0].Timestamp;
        var meanX = samples.Average(sample => (sample.Timestamp - origin).TotalSeconds);
        var meanY = samples.Average(sample => (double)sample.RemainingPercent);
        var covariance = 0d;
        var variance = 0d;
        foreach (var sample in samples)
        {
            var x = (sample.Timestamp - origin).TotalSeconds - meanX;
            covariance += x * (sample.RemainingPercent - meanY);
            variance += x * x;
        }

        if (variance <= 0)
        {
            return false;
        }

        slopePerSecond = covariance / variance;
        return slopePerSecond < -0.1 / TimeSpan.FromDays(1).TotalSeconds;
    }

    private void NotifyStatusProperties()
    {
        OnPropertyChanged(nameof(AccountStatus));
        OnPropertyChanged(nameof(LocalStatus));
        OnPropertyChanged(nameof(AccountStatusShort));
        OnPropertyChanged(nameof(LocalStatusShort));
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }

    private static string FormatTokens(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.##}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
        >= 1_000 => $"{value / 1_000d:0.##}K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };

    private static string FormatCreditBalance(string balance) =>
        decimal.TryParse(balance, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
            : balance;

    private static string FormatReset(DateTimeOffset? reset)
    {
        if (reset is null)
        {
            return "Reset time unavailable";
        }

        var relative = FormatRelativeReset(reset.Value);
        return $"{relative} · {FormatForecastDate(reset.Value)}";
    }

    private static string FormatRelativeReset(DateTimeOffset reset)
    {
        var remaining = reset - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due now";
        }

        return remaining.TotalDays >= 1
            ? $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h"
            : remaining.TotalHours >= 1
                ? $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m"
                : $"Resets in {Math.Max(1, remaining.Minutes)}m";
    }

    private static string FormatForecastDate(DateTimeOffset value) =>
        value.LocalDateTime.ToString("ddd, MMM d · HH:mm", CultureInfo.CurrentCulture);

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
            : $"{Math.Max(0, (int)duration.TotalSeconds)}s";

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }
}
