using System.ComponentModel;
using System.Windows.Media;
using LimitLens.App.ViewModels;
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
        Unloaded += (_, _) => StopObservingViewModel();
    }

    public void ApplyTaskbarTheme(bool useDarkText)
    {
        baseForeground = new SolidColorBrush(useDarkText ? DarkText : LightText);
        var track = new SolidColorBrush(useDarkText ? WpfColor.FromArgb(70, 35, 38, 44) : WpfColor.FromArgb(82, 255, 255, 255));
        baseFill = new SolidColorBrush(useDarkText ? WpfColor.FromArgb(225, 55, 59, 68) : WpfColor.FromArgb(230, 255, 255, 255));
        warningFill = new SolidColorBrush(useDarkText ? WpfColor.FromRgb(245, 106, 0) : WpfColor.FromRgb(255, 155, 56));
        criticalFill = new SolidColorBrush(useDarkText ? WpfColor.FromRgb(180, 35, 46) : WpfColor.FromRgb(255, 102, 112));
        Resources["TaskbarTextBrush"] = baseForeground;
        Resources["TaskbarTrackBrush"] = track;
        Resources["TaskbarFillBrush"] = baseFill;
        TitleText.Foreground = baseForeground;
        PercentText.Foreground = baseForeground;
        PercentText.FontWeight = System.Windows.FontWeights.SemiBold;
        UsageBar.Background = track;
        UpdateUsageColor();
    }

    private void ObserveViewModel()
    {
        StopObservingViewModel();
        observedViewModel = DataContext as DashboardViewModel;
        if (observedViewModel is not null) observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateUsageColor();
    }

    private void StopObservingViewModel()
    {
        if (observedViewModel is not null) observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        observedViewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(DashboardViewModel.ForecastRemainingPercent), StringComparison.Ordinal))
        {
            UpdateUsageColor();
        }
    }

    private void UpdateUsageColor()
    {
        var remaining = observedViewModel?.ForecastRemainingPercent ?? 100;
        var emphasis = remaining <= 10 ? criticalFill : remaining <= 25 ? warningFill : baseFill;
        UsageBar.Foreground = emphasis;
    }
}
