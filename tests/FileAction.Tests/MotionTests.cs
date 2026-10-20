using FileAction.App;
using FileAction.App.UI;

namespace FileAction.Tests;

/// <summary>
/// The motion contract: durations stay inside the design budgets and the wheel's open scale never
/// overshoots by more than 4 %. These are pure easing/duration checks, so they run without a window.
/// </summary>
public class MotionTests
{
    [Fact]
    public void Wheel_open_scale_overshoots_by_at_most_four_percent()
    {
        var peak = 0.0;
        for (var i = 0; i <= 1000; i++)
        {
            var eased = Motion.Settle.Ease(i / 1000.0);
            var scale = Motion.WheelScaleFrom + (1 - Motion.WheelScaleFrom) * eased;
            peak = Math.Max(peak, scale);
        }

        Assert.InRange(peak, 1.0, 1.04);
    }

    [Fact]
    public void Wheel_open_is_within_the_budget() => Assert.InRange(Motion.WheelOpenMs, 120, 160);

    [Fact]
    public void Wheel_motion_budgets_hold()
    {
        Assert.True(Motion.StaggerMs <= 20, "stagger must be <= 20 ms per segment");
        Assert.InRange(Motion.HighlightMs, 90, 120);
        Assert.True(Motion.SwitchMs <= 180, "mode switch must be <= 180 ms");
        Assert.InRange(Motion.WheelCloseMs, 90, 100);
        Assert.True(Motion.WheelScaleFrom < 1);
    }

    [Fact]
    public void Window_motion_budgets_hold()
    {
        Assert.InRange(Motion.WindowOpenMs, 160, 200);
        Assert.InRange(Motion.WindowCloseMs, 100, 120);
        Assert.InRange(Motion.ResizeMs, 160, 200);
        Assert.InRange(Motion.HoverMs, 80, 120);
        Assert.InRange(Motion.PressMs, 80, 120);
        Assert.InRange(Motion.RisePx, 6, 10);
        Assert.InRange(Motion.WindowScaleFrom, 0.95, 0.98);
    }

    [Fact]
    public void Settings_toggle_turns_motion_off()
    {
        var previous = Motion.Override;
        try
        {
            Motion.Override = null;
            Motion.SetSettingsEnabled(false);
            Assert.False(Motion.Enabled);
            Motion.SetSettingsEnabled(true);
            Assert.Equal(Motion.SystemAnimationsAvailable, Motion.Enabled);
        }
        finally
        {
            Motion.SetSettingsEnabled(true);
            Motion.Override = previous;
        }
    }
}
