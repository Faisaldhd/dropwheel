using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FileAction.App.UI.Tools.Pdf;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The small chrome pieces of the PDF tool windows. The merge rows use a 44 x 58 tile, so the failure
/// text must wrap and fit there instead of being clipped to "ew unava"; the page/image tile is a rounded
/// card, so it has to clip its preview with a rounded geometry (ClipToBounds alone paints square corners).
/// </summary>
[Collection(nameof(LanguageSwitching))]
public sealed class PdfTileTests
{
    private static void OnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [Fact]
    public void Merge_failure_text_fits_the_44_by_58_tile()
    {
        OnSta(() =>
        {
            var (box, _, fallback) = PdfToolChrome.Thumbnail(44, 58);
            PdfToolChrome.Fail(fallback, "tool.pdfmerge.thumbFailedShort", 44);
            box.Measure(new Size(44, 58));
            box.Arrange(new Rect(0, 0, 44, 58));
            fallback.Measure(new Size(44, 58));

            Assert.Equal("No preview", fallback.Text);
            Assert.True(fallback.DesiredSize.Width <= 44, $"the failure text is {fallback.DesiredSize.Width} DIP wide");
            Assert.True(fallback.DesiredSize.Height <= 58, $"the failure text is {fallback.DesiredSize.Height} DIP tall");
        });
    }

    [Fact]
    public void Page_tile_clips_its_preview_to_the_rounded_corner()
    {
        OnSta(() =>
        {
            var (box, _, _) = PdfToolChrome.Thumbnail(108, 132);
            box.Measure(new Size(108, 132));
            box.Arrange(new Rect(0, 0, 108, 132));

            var clip = Assert.IsType<RectangleGeometry>(box.Clip);
            Assert.Equal(new Rect(0, 0, 108, 132), clip.Rect);
            Assert.Equal(8, clip.RadiusX, 3);
            Assert.Equal(8, clip.RadiusY, 3);
        });
    }
}
