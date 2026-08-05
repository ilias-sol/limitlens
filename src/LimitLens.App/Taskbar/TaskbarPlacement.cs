namespace LimitLens.App.Taskbar;

internal static class TaskbarPlacement
{
    public static int LeftOfTray(int taskbarLeft, int trayLeft, int widgetWidth, int clearance)
        => Math.Max(0, trayLeft - taskbarLeft - widgetWidth - clearance);

    public static int CenterVertically(int taskbarTop, int taskbarBottom, int widgetHeight)
        => Math.Max(0, ((taskbarBottom - taskbarTop) - widgetHeight) / 2);
}
