using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LimitLens.App.ViewModels;
using Microsoft.Win32;

namespace LimitLens.App.Taskbar;

/// <summary>
/// Hosts a small WPF surface in Explorer's primary taskbar. The window is created as a layered popup,
/// reparented into Shell_TrayWnd, and kept immediately to the left of TrayNotifyWnd.
/// </summary>
public sealed class TaskbarWidgetService : IDisposable
{
    private const int LogicalWidth = 134;
    private const int LogicalClearance = 5;
    private const int LogicalVerticalInset = 2;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const int GwlStyle = -16;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 8;
    private const uint GaParent = 1;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmRightButtonUp = 0x0205;

    private readonly DashboardViewModel viewModel;
    private readonly DispatcherTimer healthTimer;
    private HwndSource? source;
    private TaskbarWidgetView? view;
    private IntPtr taskbar;
    private IntPtr tray;
    private bool disposed;

    public TaskbarWidgetService(DashboardViewModel viewModel)
    {
        this.viewModel = viewModel;
        healthTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => EnsureAttached(), Dispatcher.CurrentDispatcher);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        EnsureAttached();
        healthTimer.Start();
    }

    public event Action<Rect>? Clicked;
    public event Action<Rect>? ContextRequested;

    public bool IsAttached => source is not null && source.Handle != IntPtr.Zero && IsWindow(source.Handle);

    public Rect ScreenBounds
    {
        get
        {
            if (source is null || !GetWindowRect(source.Handle, out var bounds)) return Rect.Empty;
            return new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
    }

    public void EnsureAttached()
    {
        if (disposed) return;

        var currentTaskbar = FindWindow("Shell_TrayWnd", null);
        var currentTray = currentTaskbar == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(currentTaskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (currentTaskbar == IntPtr.Zero || currentTray == IntPtr.Zero || !IsWindow(currentTaskbar) || !IsWindow(currentTray))
        {
            DestroyHost();
            return;
        }

        if (source is null || !IsWindow(source.Handle) || GetAncestor(source.Handle, GaParent) != currentTaskbar || currentTaskbar != taskbar)
        {
            DestroyHost();
            taskbar = currentTaskbar;
            tray = currentTray;
            CreateHost();
        }
        else
        {
            tray = currentTray;
        }

        PositionHost();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        healthTimer.Stop();
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        DestroyHost();
    }

    private void CreateHost()
    {
        if (!GetWindowRect(taskbar, out var taskbarBounds)) return;
        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var height = Math.Max(24, taskbarBounds.Bottom - taskbarBounds.Top - (int)Math.Ceiling(LogicalVerticalInset * 2 * scale));
        var width = (int)Math.Ceiling(LogicalWidth * scale);

        var parameters = new HwndSourceParameters("LimitLensTaskbarWidget")
        {
            Width = width,
            Height = height,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow | WsExNoActivate,
            UsesPerPixelOpacity = true,
        };
        source = new HwndSource(parameters);
        view = new TaskbarWidgetView
        {
            DataContext = viewModel,
            Width = LogicalWidth,
            Height = height / scale,
        };
        view.ApplyTaskbarTheme(WindowsUsesLightTaskbar());
        source.RootVisual = view;
        source.AddHook(WindowProc);

        _ = SetParent(source.Handle, taskbar);
        if (GetAncestor(source.Handle, GaParent) != taskbar)
        {
            DestroyHost();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach Limit Lens to the Windows taskbar.");
        }
        // SetParent deliberately leaves WS_POPUP/WS_CHILD unchanged. WPF's HWND positioning hook treats
        // the reparented popup as a screen-space window, so switch it to child semantics after injection.
        // This keeps placement in taskbar-client coordinates and prevents vertical offsets on non-zero monitors.
        var style = GetWindowLongPtr(source.Handle, GwlStyle).ToInt64();
        style = (style & ~0x80000000L) | WsChild;
        _ = SetWindowLongPtr(source.Handle, GwlStyle, new IntPtr(style));
    }

    private void PositionHost()
    {
        if (source is null || !GetWindowRect(taskbar, out var taskbarBounds) || !GetWindowRect(tray, out var trayBounds)) return;
        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0) dpi = 96;
        var scale = dpi / 96d;
        var width = (int)Math.Ceiling(LogicalWidth * scale);
        var height = Math.Max(24, taskbarBounds.Bottom - taskbarBounds.Top - (int)Math.Ceiling(LogicalVerticalInset * 2 * scale));
        var x = TaskbarPlacement.LeftOfTray(taskbarBounds.Left, trayBounds.Left, width, (int)Math.Ceiling(LogicalClearance * scale));
        var y = TaskbarPlacement.CenterVertically(taskbarBounds.Top, taskbarBounds.Bottom, height);
        _ = SetWindowPos(source.Handle, IntPtr.Zero, x, y, width, height, SwpNoActivate | SwpNoZOrder | SwpShowWindow);
        _ = ShowWindow(source.Handle, SwShowNoActivate);
    }

    private void DestroyHost()
    {
        source?.RemoveHook(WindowProc);
        source?.Dispose();
        source = null;
        view = null;
        taskbar = IntPtr.Zero;
        tray = IntPtr.Zero;
    }

    private void OnClicked() => Clicked?.Invoke(ScreenBounds);
    private void OnContextRequested() => ContextRequested?.Invoke(ScreenBounds);

    private IntPtr WindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmLeftButtonUp)
        {
            handled = true;
            OnClicked();
        }
        else if (message == WmRightButtonUp)
        {
            handled = true;
            OnContextRequested();
        }
        return IntPtr.Zero;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (view is not null) view.ApplyTaskbarTheme(WindowsUsesLightTaskbar());
        EnsureAttached();
    }

    private static bool WindowsUsesLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
