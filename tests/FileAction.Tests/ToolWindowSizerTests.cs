using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The adaptive tool-window size: media aspect decides the preview box, chrome is added, the work area
/// (DPI aware) is the hard limit and tiny media is never upscaled beyond 200%.
/// </summary>
public class ToolWindowSizerTests
{
    /// <summary>Chrome and bounds used by the Crop tool.</summary>
    private static readonly ToolSpecLayout Crop = new(
        ChromeWidth: 36,
        ChromeHeight: 270,
        PreviewMinWidth: 300,
        PreviewMinHeight: 320,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 350,
        DefaultPreviewHeight: 450);

    private static ToolWindowSize Compute(double? w, double? h, double workW = 1920, double workH = 1080, double dpi = 1) =>
        ToolWindowSizer.Compute(Crop, w, h, workW, workH, dpi);

    [Fact]
    public void Portrait_media_makes_a_tall_narrow_window()
    {
        var size = Compute(3000, 4000);

        Assert.True(size.Height > size.Width);
        Assert.Equal(3000.0 / 4000.0, size.PreviewWidth / size.PreviewHeight, 3);
        Assert.True(size.Width <= 1920 * 0.95);
        Assert.True(size.Height <= 1080 * 0.90);
    }

    [Fact]
    public void Landscape_media_makes_a_wide_short_window()
    {
        var size = Compute(4000, 3000);

        // The preview follows the media; the window adds fixed chrome, so it can still be taller.
        Assert.True(size.PreviewWidth > size.PreviewHeight);
        Assert.Equal(4.0 / 3.0, size.PreviewWidth / size.PreviewHeight, 3);
    }

    [Fact]
    public void Square_media_makes_a_balanced_window()
    {
        var size = Compute(3000, 3000);

        Assert.Equal(size.PreviewWidth, size.PreviewHeight, 3);
        Assert.Equal(640, size.PreviewWidth, 3);
    }

    [Fact]
    public void Ultra_wide_media_is_clamped_so_the_controls_stay_usable()
    {
        var size = Compute(5000, 300);

        Assert.True(size.PreviewWidth <= 640);
        Assert.True(size.PreviewHeight >= 320 - 0.001);
        Assert.True(size.Height > 300);
    }

    [Fact]
    public void Ultra_tall_media_is_clamped_so_the_controls_stay_usable()
    {
        var size = Compute(300, 5000);

        Assert.True(size.PreviewHeight <= 640);
        Assert.True(size.PreviewWidth >= 300 - 0.001);
        Assert.True(size.Width > 300);
    }

    [Fact]
    public void Tiny_media_is_never_upscaled_beyond_two_hundred_percent()
    {
        var size = Compute(32, 32);

        Assert.True(size.PreviewWidth <= 32 * 2 + 0.001);
        Assert.True(size.PreviewHeight <= 32 * 2 + 0.001);
    }

    [Fact]
    public void Huge_media_is_fitted_down()
    {
        var size = Compute(12000, 9000);

        Assert.True(size.PreviewWidth <= 640);
        Assert.True(size.PreviewHeight <= 640);
        Assert.Equal(4.0 / 3.0, size.PreviewWidth / size.PreviewHeight, 3);
    }

    [Fact]
    public void A_small_work_area_at_125_percent_scales_the_preview_not_the_chrome()
    {
        var size = ToolWindowSizer.Compute(Crop, 3000, 4000, 1366, 768, 1.25);

        var workWidthDips = 1366 / 1.25;
        var workHeightDips = 768 / 1.25;
        Assert.True(size.Width <= workWidthDips * 0.95 + 0.001);
        Assert.True(size.Height <= workHeightDips * 0.90 + 0.001);
        Assert.True(size.PreviewHeight < 640);
    }

    [Fact]
    public void Unknown_media_uses_the_default_preview_box()
    {
        var size = Compute(null, null);

        Assert.Equal(350 + 36, size.Width, 3);
        Assert.Equal(450 + 270, size.Height, 3);
    }

    [Theory]
    [InlineData(3000, 4000)]
    [InlineData(4000, 3000)]
    [InlineData(3000, 3000)]
    [InlineData(5000, 300)]
    [InlineData(300, 5000)]
    [InlineData(32, 32)]
    [InlineData(12000, 9000)]
    public void Every_result_fits_the_work_area(double width, double height)
    {
        var size = Compute(width, height);

        Assert.True(size.Width <= 1920 * 0.95 + 0.001, $"{size.Width} wider than the work area");
        Assert.True(size.Height <= 1080 * 0.90 + 0.001, $"{size.Height} taller than the work area");
        Assert.True(size.Width >= Crop.ChromeWidth + 1);
        Assert.True(size.Height >= Crop.ChromeHeight + 1);
    }
}
