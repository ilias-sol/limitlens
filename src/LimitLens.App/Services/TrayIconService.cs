using System.Drawing;
using System.Windows;
using LimitLens.App.Views;
using Forms = System.Windows.Forms;

namespace LimitLens.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Icon icon;
    private readonly Action show;
    private readonly Action settings;
    private readonly Action exit;
    private readonly Func<bool> usesLightPalette;
    private TrayMenuWindow? menu;
    private bool disposed;

    public TrayIconService(
        Action show,
        Action settings,
        Action exit,
        Func<bool>? usesLightPalette = null)
    {
        this.show = show;
        this.settings = settings;
        this.exit = exit;
        this.usesLightPalette = usesLightPalette ?? (() => false);

        icon = CreateAppIcon();
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Limit Lens",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => show();
        notifyIcon.MouseUp += OnMouseUp;
    }

    public void ShowBalloon(string title, string message)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(7000);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        menu?.Dismiss();
        notifyIcon.MouseUp -= OnMouseUp;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        icon.Dispose();
        disposed = true;
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button != Forms.MouseButtons.Right)
        {
            return;
        }

        System.Windows.Application.Current.Dispatcher.BeginInvoke(ShowTrayMenu);
    }

    private void ShowTrayMenu()
    {
        if (menu is { IsVisible: true })
        {
            menu.Dismiss();
            return;
        }

        menu = new TrayMenuWindow(show, settings, exit, usesLightPalette());
        menu.Closed += (_, _) => menu = null;
        menu.ShowNearCursor();
    }

    private static Icon CreateAppIcon()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            using var associated = Icon.ExtractAssociatedIcon(executable);
            if (associated is not null)
            {
                return (Icon)associated.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
