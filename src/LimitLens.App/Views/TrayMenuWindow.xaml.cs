using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace LimitLens.App.Views;

public partial class TrayMenuWindow : Window
{
    private readonly Action showUsage;
    private readonly Action showSettings;
    private readonly Action quit;
    private bool closeStarted;

    public TrayMenuWindow(Action showUsage, Action showSettings, Action quit, bool useLightPalette)
    {
        this.showUsage = showUsage;
        this.showSettings = showSettings;
        this.quit = quit;
        InitializeComponent();
        ApplyPalette(useLightPalette);
    }

    public void ShowNearCursor()
    {
        Left = -10000;
        Top = -10000;
        Show();

        if (PresentationSource.FromVisual(this) is not HwndSource { CompositionTarget: not null } source)
        {
            return;
        }

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var cursor = Forms.Cursor.Position;
        var screen = Forms.Screen.FromPoint(cursor);
        var cursorDip = fromDevice.Transform(new System.Windows.Point(cursor.X, cursor.Y));
        var workTopLeft = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var workBottomRight = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        Left = Math.Clamp(cursorDip.X - ActualWidth + 12, workTopLeft.X + 6, workBottomRight.X - ActualWidth - 6);
        Top = Math.Clamp(cursorDip.Y - ActualHeight - 8, workTopLeft.Y + 6, workBottomRight.Y - ActualHeight - 6);
        Activate();
    }

    public void Dismiss()
    {
        if (closeStarted)
        {
            return;
        }

        closeStarted = true;
        Deactivated -= Window_Deactivated;
        Close();
    }

    private void ApplyPalette(bool light)
    {
        Resources["TrayMenuBackgroundBrush"] = Brush(light ? "#FEFFFFFF" : "#FE202124");
        Resources["TrayMenuBorderBrush"] = Brush(light ? "#D9DDE4" : "#FF494B50");
        Resources["TrayMenuTextBrush"] = Brush(light ? "#FF23272F" : "#FFF4F5F7");
        Resources["TrayMenuHoverBrush"] = Brush(light ? "#FFF0F2F5" : "#FF34363A");
        Resources["TrayMenuPressedBrush"] = Brush(light ? "#FFE5E8EC" : "#FF414349");
        Resources["TrayMenuSeparatorBrush"] = Brush(light ? "#FFE2E5EA" : "#FF45474C");
    }

    private static SolidColorBrush Brush(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));

    private void OpenUsage_Click(object sender, RoutedEventArgs e) => InvokeAndClose(showUsage);

    private void Settings_Click(object sender, RoutedEventArgs e) => InvokeAndClose(showSettings);

    private void Quit_Click(object sender, RoutedEventArgs e) => InvokeAndClose(quit);

    private void InvokeAndClose(Action action)
    {
        Dismiss();
        action();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Dismiss();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dismiss();
        }
    }
}
