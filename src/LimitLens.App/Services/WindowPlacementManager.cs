using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LimitLens.Core.Settings;
using FormsScreen = System.Windows.Forms.Screen;

namespace LimitLens.App.Services;

public static class WindowPlacementManager
{
    public static void Restore(Window window, DashboardSettings settings, DashboardWindowMode mode)
    {
        var placement = mode == DashboardWindowMode.Compact
            ? settings.CompactPlacement
            : settings.ExpandedPlacement;
        const double defaultWidth = 340d;
        var defaultHeight = mode == DashboardWindowMode.Compact ? 56d : 420d;
        var dpi = VisualTreeHelper.GetDpi(window);
        var screen = FormsScreen.AllScreens.FirstOrDefault(candidate =>
                         string.Equals(candidate.DeviceName, placement.MonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                     ?? FormsScreen.PrimaryScreen
                     ?? FormsScreen.AllScreens.First();
        var work = screen.WorkingArea;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workWidth = work.Width / dpi.DpiScaleX;
        var workHeight = work.Height / dpi.DpiScaleY;
        var width = Math.Clamp(placement.Width ?? defaultWidth, window.MinWidth, Math.Max(window.MinWidth, workWidth));
        var height = Math.Clamp(placement.Height ?? defaultHeight, window.MinHeight, Math.Max(window.MinHeight, workHeight));
        var left = placement.Left ?? workLeft + Math.Max(12, workWidth - width - 28);
        var top = placement.Top ?? workTop + 28;

        window.Width = width;
        window.Height = height;
        window.Left = Math.Clamp(left, workLeft, workLeft + Math.Max(0, workWidth - width));
        window.Top = Math.Clamp(top, workTop, workTop + Math.Max(0, workHeight - height));
    }

    public static void Save(Window window, DashboardSettings settings, DashboardWindowMode mode)
    {
        if (!window.IsLoaded || window.WindowState == WindowState.Minimized)
        {
            return;
        }

        var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
        var placement = mode == DashboardWindowMode.Compact
            ? settings.CompactPlacement
            : settings.ExpandedPlacement;
        placement.Left = bounds.Left;
        placement.Top = bounds.Top;
        placement.Width = bounds.Width;
        placement.Height = bounds.Height;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            placement.MonitorDeviceName = FormsScreen.FromHandle(handle).DeviceName;
        }
    }
}
