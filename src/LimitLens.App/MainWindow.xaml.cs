using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LimitLens.App.Services;
using LimitLens.App.ViewModels;
using LimitLens.Core.Settings;
using FormsScreen = System.Windows.Forms.Screen;

namespace LimitLens.App;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel viewModel;
    private readonly System.Windows.Threading.DispatcherTimer clockTimer;
    private readonly System.Windows.Threading.DispatcherTimer outsideClickTimer;
    private bool allowClose;
    private Rect taskbarAnchor;
    private bool leftButtonWasDown;

    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.IsCompact = false;

        clockTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => viewModel.Tick(),
            Dispatcher);
        outsideClickTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            System.Windows.Threading.DispatcherPriority.Input,
            (_, _) => DetectOutsideClick(),
            Dispatcher);
        ApplyAppearance();

        Loaded += OnLoaded;
        SourceInitialized += (_, _) => ApplyNativeAppearance();
        Activated += (_, _) => ScheduleNativeAppearance();
        Closing += OnClosing;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                leftButtonWasDown = IsLeftButtonDown();
                outsideClickTimer.Start();
                ScheduleNativeAppearance();
            }
            else
            {
                outsideClickTimer.Stop();
            }
        };
        viewModel.AppearanceChanged += OnAppearanceChanged;
        viewModel.WidgetBehaviorChanged += UpdateFlyoutHeight;
    }

    public void ShowDashboard(Rect? anchor = null, string? page = null)
    {
        if (anchor is { IsEmpty: false } bounds) taskbarAnchor = bounds;
        viewModel.IsCompact = false;
        viewModel.ShowWidgetSettings = string.Equals(page, "Settings", StringComparison.OrdinalIgnoreCase);
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        PositionAtTaskbar();
        ActivateFlyout();
        ScheduleNativeAppearance();
    }

    public void HideToTray() => Hide();
    public void PermitClose() => allowClose = true;

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Width = MinWidth = MaxWidth = 420;
        UpdateFlyoutHeight();
        ApplyAppearance();
        PositionAtTaskbar();
        ScheduleNativeAppearance();
        clockTimer.Start();
    }

    private void PositionAtTaskbar()
    {
        if (taskbarAnchor.IsEmpty) return;
        var screen = FormsScreen.FromRectangle(new System.Drawing.Rectangle(
            (int)taskbarAnchor.X,
            (int)taskbarAnchor.Y,
            Math.Max(1, (int)taskbarAnchor.Width),
            Math.Max(1, (int)taskbarAnchor.Height)));
        var dpi = VisualTreeHelper.GetDpi(this);
        var work = screen.WorkingArea;
        var anchorLeft = taskbarAnchor.Left / dpi.DpiScaleX;
        var anchorRight = taskbarAnchor.Right / dpi.DpiScaleX;
        var anchorTop = taskbarAnchor.Top / dpi.DpiScaleY;
        var anchorBottom = taskbarAnchor.Bottom / dpi.DpiScaleY;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workRight = work.Right / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workBottom = work.Bottom / dpi.DpiScaleY;

        Left = Math.Clamp(anchorRight - ActualWidth, workLeft + 6, Math.Max(workLeft + 6, workRight - ActualWidth - 6));
        if (anchorTop >= workBottom - 4)
        {
            Top = anchorTop - ActualHeight - 8;
        }
        else if (anchorBottom <= workTop + 4)
        {
            Top = anchorBottom + 8;
        }
        else
        {
            Top = Math.Clamp(anchorTop - ActualHeight - 8, workTop + 6, Math.Max(workTop + 6, workBottom - ActualHeight - 6));
            Left = anchorLeft >= workRight - 4 ? anchorLeft - ActualWidth - 8 : anchorRight + 8;
        }
    }

    private void OnAppearanceChanged()
    {
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        var light = DashboardThemeService.UsesLightPalette(viewModel.SelectedTheme);
        ApplyFlyoutPalette(light);
        ApplyNativeAppearance();
    }

    private void ApplyNativeAppearance()
    {
        var light = DashboardThemeService.UsesLightPalette(viewModel.SelectedTheme);
        _ = NativeWindowStyler.Apply(
            this,
            dark: !light,
            acrylic: true);
    }

    private void ScheduleNativeAppearance() =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (IsVisible)
            {
                ApplyNativeAppearance();
            }
        });

    private void ActivateFlyout()
    {
        Activate();
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = SetForegroundWindow(handle);
        }
    }

    private void ApplyFlyoutPalette(bool light)
    {
        SetFlyoutBrush("FlyoutSurface", light ? "#8AF3F4F5" : "#8A1A1D22");
        SetFlyoutBrush("FlyoutLayer", light ? "#4AFFFFFF" : "#4A39434C");
        SetFlyoutBrush("FlyoutLayerStrong", light ? "#62FFFFFF" : "#623D4750");
        SetFlyoutBrush("FlyoutInsight", light ? "#66FFFFFF" : "#66282C33");
        SetFlyoutBrush("FlyoutBorder", light ? "#18000000" : "#18FFFFFF");
        SetFlyoutBrush("FlyoutDivider", light ? "#12000000" : "#12FFFFFF");
        SetFlyoutBrush("FlyoutText", light ? "#FF252932" : "#FFF4F5F7");
        SetFlyoutBrush("FlyoutMuted", light ? "#FF58616E" : "#FFB2B8C2");
        SetFlyoutBrush("FlyoutDim", light ? "#FF697380" : "#FF9299A5");
        SetFlyoutBrush("FlyoutBlue", light ? "#FF3478DF" : "#FF68A4FF");
        SetFlyoutBrush("FlyoutPrediction", light ? "#FF7457C8" : "#FFB18CFF");
        SetFlyoutBrush("FlyoutTarget", light ? "#FF7F8792" : "#FF737C89");
        SetFlyoutBrush("FlyoutGrid", light ? "#14000000" : "#14FFFFFF");
        SetFlyoutBrush("FlyoutTrack", light ? "#26000000" : "#26FFFFFF");
        SetFlyoutBrush("FlyoutHover", light ? "#14000000" : "#14FFFFFF");
        SetFlyoutBrush("FlyoutPressed", light ? "#20000000" : "#20FFFFFF");
        Resources["FlyoutCardBorderThickness"] = new Thickness(0);
    }

    private void SetFlyoutBrush(string key, string value) =>
        Resources[key] = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    private void UpdateFlyoutHeight()
    {
        var height = viewModel.ShowWidgetSettings
            ? 350
            : 484;
        Height = MinHeight = MaxHeight = height;
        if (IsVisible) PositionAtTaskbar();
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        Hide();
    }

    private void DetectOutsideClick()
    {
        var leftButtonDown = IsLeftButtonDown();
        if (leftButtonDown && !leftButtonWasDown && GetCursorPos(out var cursor))
        {
            var inTaskbarIndicator = !taskbarAnchor.IsEmpty && taskbarAnchor.Contains(new System.Windows.Point(cursor.X, cursor.Y));
            var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var inFlyout = GetWindowRect(windowHandle, out var bounds)
                && cursor.X >= bounds.Left && cursor.X < bounds.Right
                && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
            if (!inTaskbarIndicator && !inFlyout) Hide();
        }
        leftButtonWasDown = leftButtonDown;
    }

    private static bool IsLeftButtonDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    private void SettingsButton_Click(object sender, RoutedEventArgs args) => viewModel.ShowWidgetSettings = !viewModel.ShowWidgetSettings;
    private void BackToGraphButton_Click(object sender, RoutedEventArgs args) => viewModel.ShowWidgetSettings = false;
    private void CloseButton_Click(object sender, RoutedEventArgs args) => Hide();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
