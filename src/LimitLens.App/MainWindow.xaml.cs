using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LimitLens.App.Services;
using LimitLens.App.Taskbar;
using LimitLens.App.ViewModels;
using LimitLens.Core.Settings;
using FormsScreen = System.Windows.Forms.Screen;

namespace LimitLens.App;

public partial class MainWindow : Window
{
    private const double FlyoutMargin = 8;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
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
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
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
        var screen = taskbarAnchor.IsEmpty
            ? FormsScreen.PrimaryScreen ?? FormsScreen.AllScreens.First()
            : FormsScreen.FromRectangle(new System.Drawing.Rectangle(
                (int)taskbarAnchor.X,
                (int)taskbarAnchor.Y,
                Math.Max(1, (int)taskbarAnchor.Width),
                Math.Max(1, (int)taskbarAnchor.Height)));
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var dpi = GetDpiForWindow(handle);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var work = screen.WorkingArea;
        var width = Math.Min(work.Width, (int)Math.Ceiling(ActualWidth * scale));
        var height = Math.Min(work.Height, (int)Math.Ceiling(ActualHeight * scale));
        var margin = (int)Math.Ceiling(FlyoutMargin * scale);
        var anchor = taskbarAnchor.IsEmpty
            ? System.Drawing.Rectangle.Empty
            : new System.Drawing.Rectangle(
                (int)taskbarAnchor.X,
                (int)taskbarAnchor.Y,
                Math.Max(1, (int)taskbarAnchor.Width),
                Math.Max(1, (int)taskbarAnchor.Height));
        var bounds = TaskbarPlacement.FlyoutBounds(
            work,
            anchor,
            new System.Drawing.Size(width, height),
            viewModel.SelectedFlyoutPosition,
            margin);

        // Screen.WorkingArea and SetWindowPos both use physical pixels. Keeping this calculation
        // entirely in that coordinate space avoids WPF logical-pixel drift at 125%/150% scaling.
        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpNoZOrder);
    }

    private void OnAppearanceChanged()
    {
        ApplyAppearance();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs args) =>
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsVisible) PositionAtTaskbar();
        });

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
        SetFlyoutBrush("FlyoutSurface", light ? "#A4F3F4F5" : "#A41A1D22");
        SetFlyoutBrush("FlyoutLayer", light ? "#64FFFFFF" : "#6439434C");
        SetFlyoutBrush("FlyoutLayerStrong", light ? "#7CFFFFFF" : "#7C3D4750");
        SetFlyoutBrush("FlyoutInsight", light ? "#80FFFFFF" : "#80282C33");
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
            ? 420
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
