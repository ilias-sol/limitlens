using System.Drawing;
using LimitLens.App.Taskbar;
using LimitLens.Core.Settings;

namespace LimitLens.Tests;

public sealed class TaskbarPlacementTests
{
    [Theory]
    [InlineData(0, 1920, 1700, 190, 5, 100, 1505)]
    [InlineData(0, 1920, 1700, 190, 5, 0, 0)]
    [InlineData(0, 1920, 1700, 190, 5, 50, 752)]
    [InlineData(-1920, 0, -220, 190, 5, 100, 1505)]
    [InlineData(1920, 3840, 3620, 134, 5, 100, 1561)]
    [InlineData(0, 2880, 2550, 285, 8, 100, 2257)]
    [InlineData(0, 1920, 1700, 190, 5, -50, 0)]
    [InlineData(0, 1920, 1700, 190, 5, 150, 1618)]
    [InlineData(0, 1920, 1700, 190, 5, 200, 1730)]
    [InlineData(0, 1920, 1700, 134, 5, 200, 1786)]
    [InlineData(-1920, 0, -220, 190, 5, 200, 1730)]
    [InlineData(1920, 3840, 3620, 134, 5, 200, 1786)]
    [InlineData(0, 2880, 2550, 285, 8, 200, 2595)]
    [InlineData(0, 1920, 1700, 190, 5, 250, 1730)]
    [InlineData(0, 100, 90, 190, 5, 100, 0)]
    [InlineData(0, 1920, 2500, 190, 5, 100, 1730)]
    public void KeepsSavedPositionsAndReachesTheFullTaskbarRightEdge(int taskbarLeft, int taskbarRight,
        int trayLeft, int widgetWidth, int clearance, int position, int expectedLeft)
    {
        Assert.Equal(expectedLeft, TaskbarPlacement.WidgetLeft(taskbarLeft, taskbarRight,
            trayLeft, widgetWidth, clearance, position));
    }

    [Theory]
    [MemberData(nameof(TaskbarEdges))]
    public void DetectsTaskbarEdge(
        Rectangle workingArea,
        Rectangle taskbarAnchor,
        int expected)
    {
        Assert.Equal((TaskbarEdge)expected, TaskbarPlacement.DetectEdge(workingArea, taskbarAnchor));
    }

    [Theory]
    [InlineData(FlyoutPosition.Left, 8)]
    [InlineData(FlyoutPosition.Center, 790)]
    [InlineData(FlyoutPosition.Right, 1572)]
    public void PositionsFlyoutAlongBottomTaskbar(FlyoutPosition position, int expectedLeft)
    {
        var bounds = TaskbarPlacement.FlyoutBounds(
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(1500, 1040, 300, 40),
            new Size(340, 484),
            position,
            8);

        Assert.Equal(new Rectangle(expectedLeft, 548, 340, 484), bounds);
    }

    [Fact]
    public void PositionsFlyoutBelowTopTaskbar()
    {
        var bounds = TaskbarPlacement.FlyoutBounds(
            new Rectangle(0, 40, 1920, 1040),
            new Rectangle(100, 0, 400, 40),
            new Size(340, 484),
            FlyoutPosition.Center,
            8);

        Assert.Equal(new Rectangle(790, 48, 340, 484), bounds);
    }

    [Theory]
    [InlineData(true, 48)]
    [InlineData(false, 1532)]
    public void PositionsFlyoutBesideVerticalTaskbar(bool taskbarOnLeft, int expectedLeft)
    {
        var workingArea = taskbarOnLeft
            ? new Rectangle(40, 0, 1880, 1080)
            : new Rectangle(0, 0, 1880, 1080);
        var taskbar = taskbarOnLeft
            ? new Rectangle(0, 800, 40, 200)
            : new Rectangle(1880, 800, 40, 200);

        var bounds = TaskbarPlacement.FlyoutBounds(
            workingArea,
            taskbar,
            new Size(340, 484),
            FlyoutPosition.Right,
            8);

        Assert.Equal(expectedLeft, bounds.Left);
        Assert.Equal(516, bounds.Top);
        Assert.True(workingArea.Contains(bounds));
    }

    public static TheoryData<Rectangle, Rectangle, int> TaskbarEdges => new()
    {
        { new Rectangle(40, 0, 1880, 1080), new Rectangle(0, 800, 40, 200), (int)TaskbarEdge.Left },
        { new Rectangle(0, 40, 1920, 1040), new Rectangle(100, 0, 400, 40), (int)TaskbarEdge.Top },
        { new Rectangle(0, 0, 1880, 1080), new Rectangle(1880, 800, 40, 200), (int)TaskbarEdge.Right },
        { new Rectangle(0, 0, 1920, 1040), new Rectangle(1500, 1040, 300, 40), (int)TaskbarEdge.Bottom },
    };
}
