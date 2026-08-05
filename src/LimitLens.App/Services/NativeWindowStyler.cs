using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LimitLens.App.Services;

public static class NativeWindowStyler
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentAcrylicBlurBehind = 4;

    public static bool Apply(
        Window window,
        bool dark = false,
        bool acrylic = false)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        // WPF creates an opaque composition target first. Making it transparent before
        // asking DWM for a system backdrop prevents the initial flat-grey fallback.
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } compositionTarget)
        {
            compositionTarget.BackgroundColor = Colors.Transparent;
        }

        var darkMode = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // DWM clips the acrylic and the WPF surface as one antialiased silhouette.
            // A second painted curve exposes the rectangular acrylic layer at its edges.
            // Use Windows' standard native curve; it visually matches the 8 px card
            // radius at the flyout's scale while preserving DWM antialiasing.
            var rounded = 2;
            _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref rounded, sizeof(int));
            var noBorder = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(handle, DwmBorderColor, ref noBorder, sizeof(int));
        }

        // DWMSBT_TRANSIENTWINDOW intentionally falls back to a flat colour while the
        // window is inactive. This taskbar flyout is opened by a WS_EX_NOACTIVATE host,
        // so use composition acrylic instead; it remains blurred regardless of focus.
        var noSystemBackdrop = 1;
        _ = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref noSystemBackdrop, sizeof(int));
        if (acrylic)
        {
            // The WPF surface supplies the tint. A nearly transparent accent tint keeps
            // the native blur visible without darkening the palette a second time.
            SetAccent(handle, AccentAcrylicBlurBehind, dark ? 0x01000000u : 0x01FFFFFFu);
        }
        else
        {
            SetAccent(handle, AccentDisabled, 0);
        }

        return true;
    }

    private static void SetAccent(IntPtr handle, int state, uint tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            GradientColor = tint,
        };
        var policySize = Marshal.SizeOf<AccentPolicy>();
        var policyPointer = Marshal.AllocHGlobal(policySize);
        try
        {
            Marshal.StructureToPtr(policy, policyPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPointer,
                SizeOfData = policySize,
            };
            _ = SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr window, ref WindowCompositionAttributeData data);

}
