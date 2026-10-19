using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>The crop rectangle rules used by the Crop tool window.</summary>
public class CropMathTests
{
    [Fact]
    public void Full_uses_the_image_size()
    {
        var rect = CropMath.Full(800, 600);

        Assert.Equal(new CropRect(0, 0, 800, 600), rect);
    }

    [Fact]
    public void SnapToRatio_makes_the_largest_centred_rectangle()
    {
        var rect = CropMath.SnapToRatio(new CropRect(0, 0, 800, 600), 800, 600, 16.0 / 9.0);

        Assert.Equal(800, rect.Width, 3);
        Assert.Equal(450, rect.Height, 3);
        Assert.Equal(0, rect.X, 3);
        Assert.Equal(75, rect.Y, 3);
    }

    [Fact]
    public void SnapToRatio_stays_inside_a_portrait_image()
    {
        var rect = CropMath.SnapToRatio(new CropRect(0, 0, 600, 900), 600, 900, 1);

        Assert.Equal(600, rect.Width, 3);
        Assert.Equal(600, rect.Height, 3);
        Assert.InRange(rect.Bottom, 0, 900);
    }

    [Fact]
    public void Clamp_keeps_the_rectangle_inside_the_image()
    {
        var rect = CropMath.Clamp(new CropRect(-50, -20, 5000, 4000), 800, 600);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(800, rect.Width);
        Assert.Equal(600, rect.Height);
    }

    [Fact]
    public void Drag_by_a_corner_resizes_without_a_ratio()
    {
        var start = new CropRect(100, 100, 200, 100);

        var rect = CropMath.Drag(CropHandle.BottomRight, start, 50, 25, 800, 600, ratio: null);

        Assert.Equal(250, rect.Width, 3);
        Assert.Equal(125, rect.Height, 3);
        Assert.Equal(100, rect.X, 3);
        Assert.Equal(100, rect.Y, 3);
    }

    [Fact]
    public void Drag_with_a_ratio_keeps_that_ratio_and_anchors_the_opposite_corner()
    {
        var start = CropMath.SnapToRatio(new CropRect(0, 0, 400, 400), 800, 600, 16.0 / 9.0);

        var rect = CropMath.Drag(CropHandle.BottomRight, start, -80, 0, 800, 600, 16.0 / 9.0);

        Assert.Equal(16.0 / 9.0, rect.Width / rect.Height, 3);
        Assert.Equal(start.X, rect.X, 3);
        Assert.Equal(start.Y, rect.Y, 3);
    }

    [Fact]
    public void Drag_never_goes_below_the_minimum_size()
    {
        var start = new CropRect(100, 100, 200, 200);

        var rect = CropMath.Drag(CropHandle.BottomRight, start, -500, -500, 800, 600, ratio: null);

        Assert.True(rect.Width >= CropMath.MinSize);
        Assert.True(rect.Height >= CropMath.MinSize);
    }

    [Fact]
    public void Pixels_rounds_and_keeps_at_least_one_pixel()
    {
        Assert.Equal((200, 100), CropMath.Pixels(new CropRect(0, 0, 200.4, 99.6)));
        Assert.Equal((1, 1), CropMath.Pixels(new CropRect(0, 0, 0.2, 0.2)));
    }

    [Fact]
    public void FromPixels_is_clamped_to_the_image()
    {
        var rect = CropMath.FromPixels(700, 500, 400, 400, 800, 600);

        Assert.True(rect.Right <= 800);
        Assert.True(rect.Bottom <= 600);
    }

    [Fact]
    public void ToolLayout_clamps_to_ninety_five_percent_width_and_ninety_percent_height()
    {
        var (w, h) = ToolLayout.Clamp(500, 745, 1920, 1080);

        Assert.Equal(500, w);
        Assert.Equal(745, h);

        var small = ToolLayout.Clamp(800, 745, 600, 500);
        Assert.Equal(570, small.Width, 3);
        Assert.Equal(450, small.Height, 3);
    }
}
