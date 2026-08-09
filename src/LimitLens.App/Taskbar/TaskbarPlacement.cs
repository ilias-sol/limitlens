using System.Drawing;
using LimitLens.Core.Settings;

namespace LimitLens.App.Taskbar;

internal enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

internal static class TaskbarPlacement
{
    public static int LeftOfTray(int taskbarLeft, int trayLeft, int widgetWidth, int clearance)
        => Math.Max(0, trayLeft - taskbarLeft - widgetWidth - clearance);

    public static int CenterVertically(int taskbarTop, int taskbarBottom, int widgetHeight)
        => Math.Max(0, ((taskbarBottom - taskbarTop) - widgetHeight) / 2);

    public static Rectangle FlyoutBounds(
        Rectangle workingArea,
        Rectangle taskbarAnchor,
        Size requestedSize,
        FlyoutPosition position,
        int margin)
    {
        margin = Math.Max(0, margin);
        var width = Math.Min(workingArea.Width, Math.Max(1, requestedSize.Width));
        var height = Math.Min(workingArea.Height, Math.Max(1, requestedSize.Height));
        var edge = DetectEdge(workingArea, taskbarAnchor);

        int left;
        int top;
        if (edge is TaskbarEdge.Top or TaskbarEdge.Bottom)
        {
            left = position switch
            {
                FlyoutPosition.Left => workingArea.Left + margin,
                FlyoutPosition.Center => workingArea.Left + Math.Max(0, (workingArea.Width - width) / 2),
                _ => workingArea.Right - width - margin,
            };
            top = edge == TaskbarEdge.Top
                ? workingArea.Top + margin
                : workingArea.Bottom - height - margin;
        }
        else
        {
            left = edge == TaskbarEdge.Left
                ? workingArea.Left + margin
                : workingArea.Right - width - margin;
            top = taskbarAnchor.IsEmpty
                ? workingArea.Bottom - height - margin
                : taskbarAnchor.Bottom - height;
        }

        var horizontalInset = Math.Min(margin, Math.Max(0, (workingArea.Width - width) / 2));
        var verticalInset = Math.Min(margin, Math.Max(0, (workingArea.Height - height) / 2));
        var minLeft = workingArea.Left + horizontalInset;
        var minTop = workingArea.Top + verticalInset;
        var maxLeft = Math.Max(minLeft, workingArea.Right - width - horizontalInset);
        var maxTop = Math.Max(minTop, workingArea.Bottom - height - verticalInset);
        left = Math.Clamp(left, minLeft, maxLeft);
        top = Math.Clamp(top, minTop, maxTop);
        return new Rectangle(left, top, width, height);
    }

    internal static TaskbarEdge DetectEdge(Rectangle workingArea, Rectangle taskbarAnchor)
    {
        if (taskbarAnchor.IsEmpty)
        {
            return TaskbarEdge.Bottom;
        }

        var distances = new[]
        {
            (Edge: TaskbarEdge.Left, Distance: Math.Abs((long)taskbarAnchor.Right - workingArea.Left)),
            (Edge: TaskbarEdge.Top, Distance: Math.Abs((long)taskbarAnchor.Bottom - workingArea.Top)),
            (Edge: TaskbarEdge.Right, Distance: Math.Abs((long)taskbarAnchor.Left - workingArea.Right)),
            (Edge: TaskbarEdge.Bottom, Distance: Math.Abs((long)taskbarAnchor.Top - workingArea.Bottom)),
        };
        return distances.MinBy(candidate => candidate.Distance).Edge;
    }
}
