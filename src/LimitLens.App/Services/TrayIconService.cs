using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsPadding = System.Windows.Forms.Padding;
using FormsToolStripItem = System.Windows.Forms.ToolStripItem;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace LimitLens.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly FormsNotifyIcon notifyIcon;
    private readonly FormsContextMenuStrip menu;
    private readonly Icon icon;
    private readonly Func<bool> usesLightPalette;
    private bool? appliedLightPalette;

    public TrayIconService(
        Action show,
        Action settings,
        Action exit,
        Func<bool>? usesLightPalette = null)
    {
        this.usesLightPalette = usesLightPalette ?? (() => false);
        icon = CreateAppIcon();
        menu = new FormsContextMenuStrip
        {
            AutoSize = true,
            DropShadowEnabled = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            MinimumSize = new Size(228, 0),
            Padding = new FormsPadding(6),
            ShowCheckMargin = false,
            ShowImageMargin = false,
        };
        menu.Items.Add(CreateItem("Open usage", (_, _) => show()));
        menu.Items.Add(CreateItem("Settings", (_, _) => settings()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator
        {
            AutoSize = false,
            Height = 9,
            Margin = FormsPadding.Empty,
        });
        menu.Items.Add(CreateItem("Quit Limit Lens", (_, _) => exit()));
        menu.Opening += (_, _) => ApplyPalette();
        menu.Opened += (_, _) => ApplyRoundedRegion();
        menu.SizeChanged += (_, _) => ApplyRoundedRegion();

        notifyIcon = new FormsNotifyIcon
        {
            Icon = icon,
            Text = "Limit Lens",
            ContextMenuStrip = menu,
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => show();
    }

    public void ShowBalloon(string title, string message)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(7000);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Region?.Dispose();
        menu.Dispose();
        icon.Dispose();
    }

    private void ApplyPalette()
    {
        var light = usesLightPalette();
        if (appliedLightPalette == light)
        {
            return;
        }

        appliedLightPalette = light;
        var palette = light ? TrayMenuPalette.Light : TrayMenuPalette.Dark;
        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Text;
        menu.Renderer = new LimitLensMenuRenderer(palette);
        foreach (FormsToolStripItem item in menu.Items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = palette.Text;
        }
    }

    private void ApplyRoundedRegion()
    {
        if (!menu.IsHandleCreated || menu.Width <= 0 || menu.Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectangle(new Rectangle(0, 0, menu.Width, menu.Height), 10);
        var previous = menu.Region;
        menu.Region = new Region(path);
        previous?.Dispose();
    }

    private static FormsToolStripMenuItem CreateItem(string text, EventHandler click)
    {
        var item = new FormsToolStripMenuItem(text)
        {
            AutoSize = false,
            Height = 34,
            Padding = new FormsPadding(10, 0, 10, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 216,
        };
        item.Click += click;
        return item;
    }

    private static Icon CreateAppIcon()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var associated = Icon.ExtractAssociatedIcon(executable);
            if (associated is not null)
            {
                return (Icon)associated.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed record TrayMenuPalette(
        Color Background,
        Color Hover,
        Color Border,
        Color Separator,
        Color Text)
    {
        public static TrayMenuPalette Dark { get; } = new(
            Color.FromArgb(255, 30, 30, 30),
            Color.FromArgb(255, 52, 52, 52),
            Color.FromArgb(255, 76, 76, 76),
            Color.FromArgb(255, 76, 76, 76),
            Color.FromArgb(255, 246, 246, 246));

        public static TrayMenuPalette Light { get; } = new(
            Color.FromArgb(255, 248, 248, 248),
            Color.FromArgb(255, 231, 231, 231),
            Color.FromArgb(255, 190, 190, 190),
            Color.FromArgb(255, 214, 214, 214),
            Color.FromArgb(255, 28, 28, 28));
    }

    private sealed class LimitLensMenuRenderer(TrayMenuPalette palette) : System.Windows.Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(palette.Background);
        }

        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(palette.Hover);
            using var path = CreateRoundedRectangle(new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2), 6);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = palette.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var background = new SolidBrush(palette.Background);
            e.Graphics.FillRectangle(background, new Rectangle(Point.Empty, e.Item.Size));
            using var pen = new Pen(palette.Separator);
            var y = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }

        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(palette.Border);
            using var path = CreateRoundedRectangle(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10);
            e.Graphics.DrawPath(pen, path);
        }
    }
}
