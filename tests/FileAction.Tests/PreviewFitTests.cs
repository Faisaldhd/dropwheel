using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The preview geometry shared by Edit Photos, Annotate and Add Background: a bitmap is fitted and
/// centred, never enlarged past 200% of its own pixels, and never shrunk below 100% when the preview can
/// hold it. The four audited sizes are covered explicitly; no window or bitmap is involved.
/// </summary>
public class PreviewFitTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(16, 60, 32, 120, 204, 130)]
    [InlineData(83, 60, 166, 120, 137, 130)]
    [InlineData(120, 120, 240, 240, 100, 70)]
    public void Tiny_bitmaps_are_capped_at_200_percent_and_centred(
        int pixelWidth, int pixelHeight, double width, double height, double originX, double originY)
    {
        var fit = PreviewFit.Fit(440, 380, pixelWidth, pixelHeight);

        Assert.Equal(PreviewFit.MaxUpscale, fit.Scale, 9);
        Assert.Equal(width, fit.Width, 9);
        Assert.Equal(height, fit.Height, 9);
        Assert.Equal(originX, fit.OriginX, 9);
        Assert.Equal(originY, fit.OriginY, 9);
        Assert.Equal(pixelWidth * 2, fit.Width, 9);
        Assert.Equal(pixelHeight * 2, fit.Height, 9);
    }

    [Fact]
    public void A_640x420_bitmap_larger_than_the_preview_is_fitted_and_letterboxed()
    {
        var fit = PreviewFit.Fit(440, 380, 640, 420);

        Assert.Equal(0.6875, fit.Scale, 9);
        Assert.Equal(440, fit.Width, 9);
        Assert.Equal(288.75, fit.Height, 9);
        Assert.Equal(0, fit.OriginX, 9);
        Assert.Equal(45.625, fit.OriginY, 9);
        Assert.True(fit.Height < 380, "the leftover height is the letterbox");
    }

    [Fact]
    public void A_640x420_bitmap_that_fits_is_never_shown_below_1x()
    {
        var fit = PreviewFit.Fit(1000, 700, 640, 420);

        Assert.Equal(1.5625, fit.Scale, 9);
        Assert.Equal(1000, fit.Width, 9);
        Assert.Equal(656.25, fit.Height, 9);
        Assert.Equal(0, fit.OriginX, 9);
        Assert.Equal(21.875, fit.OriginY, 9);
        Assert.InRange(fit.Scale, 1.0, PreviewFit.MaxUpscale);
    }

    [Theory]
    [InlineData(16, 60)]
    [InlineData(83, 60)]
    [InlineData(120, 120)]
    [InlineData(640, 420)]
    public void Every_result_preserves_the_aspect_and_stays_inside_the_preview(int pixelWidth, int pixelHeight)
    {
        var fit = PreviewFit.Fit(440, 380, pixelWidth, pixelHeight);

        Assert.True(fit.Width <= 440 + Tolerance, $"{fit.Width} wider than the preview");
        Assert.True(fit.Height <= 380 + Tolerance, $"{fit.Height} taller than the preview");
        Assert.True(fit.OriginX >= -Tolerance, $"{fit.OriginX} left of the preview");
        Assert.True(fit.OriginY >= -Tolerance, $"{fit.OriginY} above the preview");
        Assert.Equal(pixelWidth / (double)pixelHeight, fit.Width / fit.Height, 9);
        Assert.True(fit.Width <= pixelWidth * PreviewFit.MaxUpscale + Tolerance);
        Assert.True(fit.Height <= pixelHeight * PreviewFit.MaxUpscale + Tolerance);
    }

    [Fact]
    public void The_two_letterbox_bands_are_equal()
    {
        var fit = PreviewFit.Fit(440, 380, 640, 420);

        Assert.Equal(fit.OriginY, 380 - (fit.OriginY + fit.Height), 9);
    }

    [Theory]
    [InlineData(0, 380)]
    [InlineData(440, 0)]
    public void An_empty_preview_has_no_rendered_size(double viewWidth, double viewHeight)
    {
        var fit = PreviewFit.Fit(viewWidth, viewHeight, 16, 60);

        Assert.Equal(1, fit.Scale);
        Assert.Equal(0, fit.Width);
        Assert.Equal(0, fit.Height);
        Assert.Equal(0, fit.OriginX);
        Assert.Equal(0, fit.OriginY);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(16, 0)]
    public void An_empty_bitmap_has_no_rendered_size(int pixelWidth, int pixelHeight)
    {
        var fit = PreviewFit.Fit(440, 380, pixelWidth, pixelHeight);

        Assert.Equal(1, fit.Scale);
        Assert.Equal(0, fit.Width);
        Assert.Equal(0, fit.Height);
    }

    [Fact]
    public void ScaleFor_matches_the_full_fit()
    {
        Assert.Equal(
            PreviewFit.Fit(440, 380, 120, 120).Scale,
            PreviewFit.ScaleFor(440, 380, 120, 120),
            9);
    }
}
