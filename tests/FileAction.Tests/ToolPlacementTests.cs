using System.Windows;
using FileAction.App.UI.Tools;

namespace FileAction.Tests;

public class ToolPlacementTests
{
    private static readonly Rect Work = new(0, 0, 1920, 1080);

    [Fact]
    public void Opens_below_and_to_the_right_of_the_pointer()
    {
        var at = ToolPlacement.NearCursor(new Point(100, 100), 400, 300, Work);

        Assert.Equal(118, at.X);
        Assert.Equal(118, at.Y);
    }

    [Fact]
    public void Flips_left_and_up_when_the_card_would_leave_the_work_area()
    {
        var at = ToolPlacement.NearCursor(new Point(1900, 1070), 400, 300, Work);

        Assert.Equal(1482, at.X);
        Assert.Equal(752, at.Y);
    }

    [Fact]
    public void Clamps_an_oversized_card_inside_the_work_area()
    {
        var at = ToolPlacement.NearCursor(new Point(100, 100), 3000, 2000, Work);

        Assert.Equal(0, at.X);
        Assert.Equal(0, at.Y);
    }

    [Fact]
    public void Keeps_negative_monitor_coordinates()
    {
        var work = new Rect(-1920, 0, 1920, 1080);
        var at = ToolPlacement.NearCursor(new Point(-1000, 100), 400, 300, work);

        Assert.Equal(-982, at.X);
        Assert.Equal(118, at.Y);
    }
}
