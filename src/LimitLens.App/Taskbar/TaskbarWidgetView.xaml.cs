using System.ComponentModel;
using System.Windows.Media;
using LimitLens.App.ViewModels;
using LimitLens.Core.Settings;
using WpfColor = System.Windows.Media.Color;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace LimitLens.App.Taskbar;

public partial class TaskbarWidgetView : WpfUserControl
{
    private static readonly WpfColor DarkText = WpfColor.FromRgb(35, 38, 44);
    private static readonly WpfColor LightText = WpfColor.FromRgb(245, 246, 248);
    private DashboardViewModel? observedViewModel;
    private SolidColorBrush baseFill = new(LightText);
    private SolidColorBrush baseForeground = new(LightText);
    private SolidColorBrush warningFill = new(WpfColor.FromRgb(255, 155, 56));
    private SolidColorBrush criticalFill = new(WpfColor.FromRgb(255, 102, 112));

    public TaskbarWidgetView()
    {
        InitializeComponent();
        MouseEnter += (_, _) => HoverSurface.Background = new SolidColorBrush(WpfColor.FromArgb(24, 128, 128, 128));
        MouseLeave += (_, _) => HoverSurface.Background = System.Windows.Media.Brushes.Transparent;
        DataContextChanged += (_, _) => ObserveViewModel();
        Loaded += (_, _) => ObserveViewModel();
        Unloaded += (_, _) => StopObservingViewModel();
    }

    public void ApplyTaskbarTheme(bool useDarkText)
    {
        baseForeground = new SolidColorBrush(useDarkText ? DarkText : LightText);
        var track = new SolidColorBrush(useDarkText ? WpfColor.FromArgb(70, 35, 38, 44) : WpfColor.FromArgb(82, 255, 255, 255));
        baseFill = new SolidColorBrush(useDarkText ? WpfColor.FromArgb(225, 55, 59, 68) : WpfColor.FromArgb(230, 255, 255, 255));
        warningFill = new SolidColorBrush(useDarkText ? WpfColor.FromRgb(245, 106, 0) : WpfColor.FromRgb(255, 155, 56));
        criticalFill = new SolidColorBrush(useDarkText ? WpfColor.FromRgb(180, 35, 46) : WpfColor.FromRgb(255, 102, 112));
        Resources["TaskbarTrackBrush"] = track;
        Resources["TaskbarFillBrush"] = baseFill;
        PercentText.FontWeight = System.Windows.FontWeights.SemiBold;
        UsageBar.Background = track;
        SecondaryUsageBar.Background = track;
        UpdateAppearance();
    }

    private void ObserveViewModel()
    {
        StopObservingViewModel();
        observedViewModel = DataContext as DashboardViewModel;
        if (observedViewModel is not null) observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateAppearance();
    }

    private void StopObservingViewModel()
    {
        if (observedViewModel is not null) observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        observedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(DashboardViewModel.TaskbarPrimaryLimit) or nameof(DashboardViewModel.TaskbarSecondaryLimit))
        {
            UpdateUsageColor();
        }
        else if (args.PropertyName is nameof(DashboardViewModel.TaskbarTextColorMode)
            or nameof(DashboardViewModel.TaskbarBarColorMode)
            or nameof(DashboardViewModel.TaskbarCustomTextColor)
            or nameof(DashboardViewModel.TaskbarCustomBarColor))
        {
            UpdateAppearance();
        }
    }

    private void UpdateAppearance()
    {
        var foreground = ColorOverride(observedViewModel?.TaskbarTextColorMode,
            observedViewModel?.TaskbarCustomTextColor) ?? baseForeground;
        Resources["TaskbarTextBrush"] = foreground;
        TitleText.Foreground = PercentText.Foreground = foreground;
        SecondaryTitleText.Foreground = SecondaryPercentText.Foreground = foreground;
        var bar = ColorOverride(observedViewModel?.TaskbarBarColorMode,
            observedViewModel?.TaskbarCustomBarColor);
        var track = bar is null ? (SolidColorBrush)Resources["TaskbarTrackBrush"]
            : new SolidColorBrush(WpfColor.FromArgb(64, bar.Color.R, bar.Color.G, bar.Color.B));
        UsageBar.Background = SecondaryUsageBar.Background = track;
        UpdateUsageColor();
    }

    private void UpdateUsageColor()
    {
        UsageBar.Foreground = Fill(observedViewModel?.TaskbarPrimaryLimit.RemainingPercent);
        SecondaryUsageBar.Foreground = Fill(observedViewModel?.TaskbarSecondaryLimit.RemainingPercent);
    }

    private SolidColorBrush Fill(int? remaining) => ColorOverride(observedViewModel?.TaskbarBarColorMode,
        observedViewModel?.TaskbarCustomBarColor)
        ?? (remaining is <= 10 ? criticalFill : remaining is <= 25 ? warningFill : baseFill);

    private static SolidColorBrush? ColorOverride(TaskbarColorMode? mode, string? hex) => mode switch
    {
        TaskbarColorMode.White => System.Windows.Media.Brushes.White,
        TaskbarColorMode.Black => System.Windows.Media.Brushes.Black,
        TaskbarColorMode.Gray => System.Windows.Media.Brushes.Gray,
        TaskbarColorMode.Custom when TaskbarColors.TryNormalizeHex(hex, out var normalized) =>
            new SolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(normalized)),
        _ => null,
    };
}
