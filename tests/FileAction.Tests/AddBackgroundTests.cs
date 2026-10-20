using FileAction.App.UI.Tools.Image.Frame;
using FileAction.Core;
using ImageMagick;
using ImageMagick.Drawing;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The Add Background geometry: padded canvas, ratio crop, proportional scaling to the original
/// resolution, cover-fit for a background image and the limits that keep a huge export in memory.
/// No window, no bitmap and no Magick call is involved.
/// </summary>
public class AddBackgroundTests
{
    [Fact]
    public void Auto_keeps_the_padded_square_and_centres_the_photo()
    {
        var composition = AddBackground.Compose(1200, 1200, 160, 64, 40, BackgroundRatio.Auto);

        Assert.Equal(1520, composition.CanvasWidth);
        Assert.Equal(1520, composition.CanvasHeight);
        Assert.Equal(1520, composition.BackgroundWidth);
        Assert.Equal(1520, composition.BackgroundHeight);
        Assert.Equal(0, composition.BackgroundX);
        Assert.Equal(0, composition.BackgroundY);
        Assert.Equal(160, composition.ImageX);
        Assert.Equal(160, composition.ImageY);
        Assert.Equal(1200, composition.ImageWidth);
        Assert.Equal(1200, composition.ImageHeight);
        Assert.Equal(64, composition.CornerRadius);
    }

    [Fact]
    public void Ratio_crops_the_padded_composition_around_the_photo()
    {
        // 1200 Ã— 1200 with 160 of padding is 1520 Ã— 1520; 16:9 keeps its width and trims the height.
        var composition = AddBackground.Compose(1200, 1200, 160, 64, 40, BackgroundRatio.Wide);

        Assert.Equal(1520, composition.CanvasWidth);
        Assert.Equal(855, composition.CanvasHeight);
        Assert.Equal(1520, composition.BackgroundWidth);
        Assert.Equal(1520, composition.BackgroundHeight);
        Assert.Equal(0, composition.BackgroundX);
        Assert.Equal(-332, composition.BackgroundY);
        Assert.Equal(160, composition.ImageX);
        Assert.Equal(160 - 332, composition.ImageY); // the photo is trimmed top and bottom
        Assert.Equal(16.0 / 9.0, composition.CanvasWidth / (double)composition.CanvasHeight, 1);
    }

    [Fact]
    public void Portrait_ratio_crops_a_landscape_photo_sideways()
    {
        // 1600 Ã— 900 with 100 of padding is 1800 Ã— 1100; 4:5 keeps the height and trims the width.
        var composition = AddBackground.Compose(1600, 900, 100, 40, 20, BackgroundRatio.Portrait);

        Assert.Equal(880, composition.CanvasWidth);
        Assert.Equal(1100, composition.CanvasHeight);
        Assert.Equal(-460, composition.BackgroundX);
        Assert.Equal(0, composition.BackgroundY);
        Assert.Equal(100 - 460, composition.ImageX);
        Assert.Equal(100, composition.ImageY);
        Assert.Equal(0.8, composition.CanvasWidth / (double)composition.CanvasHeight, 1);
    }

    [Fact]
    public void Square_ratio_centres_a_landscape_photo_that_has_no_padding()
    {
        var composition = AddBackground.Compose(800, 600, 0, 0, 0, BackgroundRatio.Square);

        Assert.Equal(600, composition.CanvasWidth);
        Assert.Equal(600, composition.CanvasHeight);
        Assert.Equal(-100, composition.BackgroundX);
        Assert.Equal(800, composition.BackgroundWidth);
        Assert.Equal(-100, composition.ImageX);
        Assert.Equal(0, composition.ImageY);
    }

    [Fact]
    public void The_photo_stays_centred_in_every_ratio()
    {
        foreach (var ratio in Enum.GetValues<BackgroundRatio>())
        {
            var composition = AddBackground.Compose(3000, 2000, 160, 64, 40, ratio);

            Assert.InRange(composition.ImageX + composition.ImageWidth / 2.0, composition.CanvasWidth / 2.0 - 1, composition.CanvasWidth / 2.0 + 1);
            Assert.InRange(composition.ImageY + composition.ImageHeight / 2.0, composition.CanvasHeight / 2.0 - 1, composition.CanvasHeight / 2.0 + 1);
        }
    }

    [Fact]
    public void Cover_fit_keeps_the_aspect_and_splits_the_overflow()
    {
        var landscape = AddBackground.CoverRect(1000, 500, 800, 800);

        Assert.Equal(new BackgroundRect(-400, 0, 1600, 800), landscape);

        var portrait = AddBackground.CoverRect(500, 1000, 800, 800);

        Assert.Equal(new BackgroundRect(0, -400, 800, 1600), portrait);
    }

    [Fact]
    public void Cover_fit_is_exact_when_the_aspect_already_matches()
    {
        Assert.Equal(new BackgroundRect(0, 0, 400, 300), AddBackground.CoverRect(800, 600, 400, 300));
    }

    [Fact]
    public void Cover_fit_never_leaves_a_gap()
    {
        foreach (var (sourceW, sourceH) in new[] { (1, 1), (3000, 100), (100, 3000), (1920, 1080) })
        {
            var rect = AddBackground.CoverRect(sourceW, sourceH, 640, 480);

            Assert.True(rect.Width >= 640, $"{sourceW}Ã—{sourceH}: {rect}");
            Assert.True(rect.Height >= 480, $"{sourceW}Ã—{sourceH}: {rect}");
            Assert.True(rect.X <= 0, $"{sourceW}Ã—{sourceH}: {rect}");
            Assert.True(rect.Y <= 0, $"{sourceW}Ã—{sourceH}: {rect}");
            Assert.True(rect.Right >= 640, $"{sourceW}Ã—{sourceH}: {rect}");
            Assert.True(rect.Bottom >= 480, $"{sourceW}Ã—{sourceH}: {rect}");
        }
    }

    [Fact]
    public void Slider_values_are_pixels_of_the_preview_and_grow_by_the_original_scale()
    {
        Assert.Equal(2.0, AddBackground.ScaleFor(4096, 2048), 3);
        Assert.Equal(1.0, AddBackground.ScaleFor(2048, 2048), 3);
        Assert.Equal(1.0, AddBackground.ScaleFor(0, 1024), 3);
        Assert.Equal(1.0, AddBackground.ScaleFor(4096, 0), 3);

        Assert.Equal(320, AddBackground.Scale(160, 2.0));
        Assert.Equal(1172, AddBackground.Scale(400, 2.93));
        Assert.Equal(0, AddBackground.Scale(-5, 2.0));
        Assert.Equal(0, AddBackground.Scale(160, 0));
    }

    [Fact]
    public void The_export_scale_reproduces_the_preview_proportions()
    {
        // The preview is 2048 wide; the photo is 4096; every value doubles on the way out.
        var preview = AddBackground.Compose(2048, 1536, 160, 64, 40, BackgroundRatio.Auto);
        var export = AddBackground.Compose(
            4096, 3072,
            AddBackground.Scale(160, AddBackground.ScaleFor(4096, 2048)),
            AddBackground.Scale(64, AddBackground.ScaleFor(4096, 2048)),
            AddBackground.Scale(40, AddBackground.ScaleFor(4096, 2048)),
            BackgroundRatio.Auto);

        Assert.Equal(preview.CanvasWidth * 2, export.CanvasWidth);
        Assert.Equal(preview.CanvasHeight * 2, export.CanvasHeight);
        Assert.Equal(preview.ImageX * 2, export.ImageX);
        Assert.Equal(preview.CornerRadius * 2, export.CornerRadius);
        Assert.Equal(preview.ShadowBlur * 2, export.ShadowBlur);
        Assert.Equal(preview.ShadowOpacity, export.ShadowOpacity, 3);
    }

    [Fact]
    public void Tiny_padding_stays_untouched()
    {
        Assert.Equal(160, AddBackground.LimitPadding(2048, 2048, 160));
        Assert.Equal(0, AddBackground.LimitPadding(2000, 2000, 0));
        Assert.Equal(0, AddBackground.LimitPadding(2000, 2000, -40));
    }

    [Fact]
    public void A_huge_photo_keeps_its_pixels_but_the_frame_is_capped()
    {
        // 9000 Ã— 7000 is 63 MP; only the frame can be trimmed to stay inside the 64 MP canvas limit.
        var padding = AddBackground.LimitPadding(9000, 7000, 469);

        Assert.True(padding is > 0 and < 469, padding.ToString());
        var width = 9000 + 2 * padding;
        var height = 7000 + 2 * padding;
        Assert.True((long)width * height <= AddBackground.MaxCanvasPixels, $"{width} Ã— {height} is over the cap");
        Assert.True(width <= AddBackground.MaxCanvasSide);
    }

    [Fact]
    public void A_photo_that_is_already_over_the_pixel_cap_gets_no_frame()
    {
        Assert.Equal(0, AddBackground.LimitPadding(30000, 20000, 400));
    }

    [Fact]
    public void Compose_caps_the_frame_and_keeps_the_photo_size()
    {
        var composition = AddBackground.Compose(6000, 6000, 4000, 2000, 500, BackgroundRatio.Auto);

        Assert.Equal(6000, composition.ImageWidth);
        Assert.Equal(6000, composition.ImageHeight);
        Assert.True(composition.CanvasWidth < 6000 + 2 * 4000);
        Assert.True((long)composition.CanvasWidth * composition.CanvasHeight <= AddBackground.MaxCanvasPixels);
        Assert.True(composition.CornerRadius <= 3000);
        Assert.True(composition.ShadowBlur < 500);
        // Corners and shadow shrank with the padding instead of becoming relatively bigger.
        Assert.True(composition.CornerRadius < composition.ImageWidth / 3);
    }

    [Fact]
    public void Corners_are_never_rounder_than_half_the_short_side()
    {
        var composition = AddBackground.Compose(100, 4000, 0, 400, 0, BackgroundRatio.Auto);

        Assert.Equal(50, composition.CornerRadius);
    }

    [Fact]
    public void No_shadow_means_no_blur_offset_or_opacity()
    {
        var composition = AddBackground.Compose(1200, 800, 40, 20, 0, BackgroundRatio.Auto);

        Assert.Equal(0, composition.ShadowBlur);
        Assert.Equal(0, composition.ShadowOffsetY);
        Assert.Equal(0, composition.ShadowOpacity);
        Assert.Equal(0, AddBackground.ShadowOpacityFor(0, 1000));
    }

    [Fact]
    public void Shadow_strength_grows_with_the_blur_but_stays_readable()
    {
        var faint = AddBackground.ShadowOpacityFor(5, 1000);
        var strong = AddBackground.ShadowOpacityFor(50, 1000);
        var huge = AddBackground.ShadowOpacityFor(5000, 1000);

        Assert.True(faint > 0, faint.ToString());
        Assert.True(strong > faint, $"{strong} should be stronger than {faint}");
        Assert.InRange(huge, 0.0, 0.42);
    }

    [Fact]
    public void Shadow_offset_follows_the_blur()
    {
        Assert.Equal(0, AddBackground.ShadowOffsetFor(0));
        Assert.Equal(1, AddBackground.ShadowOffsetFor(1));
        Assert.Equal(7, AddBackground.ShadowOffsetFor(40));
        Assert.True(AddBackground.ShadowOffsetFor(100) > AddBackground.ShadowOffsetFor(40));
    }

    [Fact]
    public void Ratio_values_match_the_chips()
    {
        Assert.Equal(0, AddBackground.RatioValue(BackgroundRatio.Auto));
        Assert.Equal(1.0, AddBackground.RatioValue(BackgroundRatio.Square));
        Assert.Equal(16.0 / 9.0, AddBackground.RatioValue(BackgroundRatio.Wide), 6);
        Assert.Equal(0.8, AddBackground.RatioValue(BackgroundRatio.Portrait), 6);
    }

    [Fact]
    public void There_are_nine_distinct_gradients_and_nine_distinct_solids()
    {
        var presets = Enum.GetValues<GradientPreset>();
        Assert.Equal(9, presets.Length);

        var gradients = presets.Select(AddBackground.Gradient).ToList();
        Assert.All(gradients, stops => Assert.NotEqual(stops.Start, stops.End));
        Assert.Equal(9, gradients.Distinct().Count());

        var solids = AddBackground.SolidColors;
        Assert.Equal(9, solids.Count);
        Assert.Equal(9, solids.Distinct().Count());
    }

    [Fact]
    public void The_smallest_photo_keeps_a_usable_canvas()
    {
        var composition = AddBackground.Compose(1, 1, 0, 0, 0, BackgroundRatio.Auto);

        Assert.Equal(1, composition.CanvasWidth);
        Assert.Equal(1, composition.CanvasHeight);
        Assert.Equal(0, composition.ImageX);
        Assert.Equal(0, composition.CornerRadius);
    }

    [Fact]
    public void Zero_sized_input_never_produces_a_zero_canvas()
    {
        var composition = AddBackground.Compose(0, 0, 0, 0, 0, BackgroundRatio.Square);

        Assert.True(composition.CanvasWidth >= 1);
        Assert.True(composition.CanvasHeight >= 1);
        Assert.True(composition.ImageWidth >= 1);
        Assert.True(composition.ImageHeight >= 1);
    }

    [Fact]
    public void The_window_and_primary_labels_resolve_in_both_languages()
    {
        var spec = AddBackgroundTool.Instance;

        Assert.Equal("tools.addbackground", spec.Id);
        Assert.Equal("tool.addbackground.title", spec.TitleKey);
        Assert.Equal("tool.addbackground.apply", spec.PrimaryLabelKey);

        var en = Loc.Table(Language.English);
        var ar = Loc.Table(Language.Arabic);
        foreach (var key in new[] { spec.TitleKey, spec.PrimaryLabelKey, "tool.addbackground.suffix" })
        {
            Assert.True(en.ContainsKey(key), $"en.json is missing {key}");
            Assert.True(ar.ContainsKey(key), $"ar.json is missing {key}");
            Assert.NotEqual(en[key], ar[key]);
        }

        Assert.Equal("Add BG", en[spec.TitleKey]);
        Assert.Equal("Save with Background", en[spec.PrimaryLabelKey]);
    }

    [Fact]
    public void The_defaults_behind_Reset_are_the_documented_ones()
    {
        Assert.Equal(160, AddBackground.DefaultPadding);
        Assert.Equal(64, AddBackground.DefaultCorners);
        Assert.Equal(40, AddBackground.DefaultShadow);
        Assert.Equal(400, AddBackground.MaxPadding);
        Assert.Equal(400, AddBackground.MaxCorners);
        Assert.Equal(100, AddBackground.MaxShadow);
        Assert.Equal(GradientPreset.Violet, Enum.GetValues<GradientPreset>()[0]);
        Assert.Equal(0, AddBackground.RatioValue(BackgroundRatio.Auto));
    }

    [Fact]
    public void The_tool_accepts_one_photo_and_rejects_other_inputs()
    {
        var spec = AddBackgroundTool.Instance;

        Assert.True(spec.Applies([new FileItem("photo.jpg")]));
        Assert.False(spec.Applies([new FileItem("report.pdf")]));
        Assert.False(spec.Applies([new FileItem("one.png"), new FileItem("two.png")]));
        Assert.False(spec.Applies([]));
    }
}

/// <summary>
/// Real output of <see cref="AddBackgroundRenderer"/>: every test renders a file and asserts the PNG's
/// pixel size and the exact colour at coordinates taken from <see cref="AddBackground.Compose"/>, so the
/// geometry and the pixels are checked together.
/// </summary>
public sealed class AddBackgroundRenderTests : IDisposable
{
    private static readonly MagickColor White = new(255, 255, 255);
    private static readonly MagickColor Red = new(255, 0, 0);
    private static readonly MagickColor Black = new(0, 0, 0);
    private static readonly BackgroundColor WhiteFill = new(255, 255, 255);
    private static readonly BackgroundColor BlackFill = new(0, 0, 0);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dropwheel-addbg-tests", Guid.NewGuid().ToString("N"));

    public AddBackgroundRenderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => OutputPaths.TryDelete(_directory);

    [Fact]
    public void Render_writes_the_composed_size_with_the_backdrop_around_the_photo()
    {
        var source = Solid("photo.png", 320, 200, MagickColors.RebeccaPurple);
        var composition = AddBackground.Compose(320, 200, 20, 0, 0, BackgroundRatio.Auto);

        var target = Render(source, BackgroundFill.OfColor(WhiteFill), 20, 0, 0, BackgroundRatio.Auto);

        using var image = new MagickImage(target);
        Assert.Equal(composition.CanvasWidth, (int)image.Width);
        Assert.Equal(composition.CanvasHeight, (int)image.Height);
        Assert.Equal(360, (int)image.Width);
        Assert.Equal(240, (int)image.Height);
        Assert.Equal(MagickFormat.Png, image.Format);

        AssertPixel(target, 0, 0, White);
        AssertPixel(target, 359, 239, White);
        AssertPixel(target, 20, 20, MagickColors.RebeccaPurple);
        AssertPixel(target, 339, 219, MagickColors.RebeccaPurple);
    }

    [Fact]
    public void Render_rounds_the_corners_so_the_backdrop_shows_through()
    {
        var source = Solid("photo.png", 200, 200, Red);

        var target = Render(source, BackgroundFill.OfColor(WhiteFill), 10, 60, 0, BackgroundRatio.Auto);

        // 10 px of padding and a 60 px radius: the exact corner pixel is outside the rounded photo.
        AssertPixel(target, 10, 10, White);
        AssertPixel(target, 70, 70, Red);
        AssertPixel(target, 110, 40, Red);
    }

    [Fact]
    public void Render_crops_the_composition_to_the_ratio()
    {
        var source = Solid("photo.png", 200, 100, Red);
        var composition = AddBackground.Compose(200, 100, 100, 0, 0, BackgroundRatio.Wide);

        var target = Render(source, BackgroundFill.OfColor(BlackFill), 100, 0, 0, BackgroundRatio.Wide);

        using var image = new MagickImage(target);
        Assert.Equal(400, composition.CanvasWidth);
        Assert.Equal(225, composition.CanvasHeight);
        Assert.Equal(composition.CanvasWidth, (int)image.Width);
        Assert.Equal(composition.CanvasHeight, (int)image.Height);

        AssertPixel(target, 0, 0, Black);
        AssertPixel(target, 399, 224, Black);
        AssertPixel(target, 200, 110, Red);
    }

    [Fact]
    public void Render_covers_the_canvas_with_a_background_image()
    {
        var green = new MagickColor(0, 200, 0);
        var backdrop = Solid("backdrop.png", 400, 100, green);
        var source = Solid("photo.png", 200, 200, Red);

        var target = Render(source, BackgroundFill.OfImage(backdrop, blur: false), 10, 0, 0, BackgroundRatio.Auto);

        // A 4:1 backdrop over a square canvas is cropped, not letterboxed: both far corners are green.
        AssertPixel(target, 0, 0, green);
        AssertPixel(target, 219, 219, green);
        AssertPixel(target, 110, 110, Red);
    }

    [Fact]
    public void Render_lets_transparent_source_pixels_show_the_backdrop()
    {
        var source = Path.Combine(_directory, "transparent.png");
        using (var image = new MagickImage(MagickColors.Transparent, 100, 100))
        {
            image.Draw(new Drawables().FillColor(Red).Rectangle(50, 0, 99, 99));
            image.Write(source, MagickFormat.Png);
        }

        var target = Render(source, BackgroundFill.OfColor(WhiteFill), 10, 0, 0, BackgroundRatio.Auto);

        AssertPixel(target, 20, 50, White);
        AssertPixel(target, 100, 50, Red);
    }

    [Fact]
    public void Render_keeps_alpha_when_the_backdrop_is_transparent()
    {
        var backdrop = Path.Combine(_directory, "clear.png");
        using (var clear = new MagickImage(MagickColors.Transparent, 50, 50))
            clear.Write(backdrop, MagickFormat.Png);
        var source = Solid("photo.png", 100, 100, Red);

        var target = Render(source, BackgroundFill.OfImage(backdrop, blur: false), 20, 0, 0, BackgroundRatio.Auto);

        using var image = new MagickImage(target);
        Assert.True(image.HasAlpha, "the saved PNG should keep an alpha channel");

        // Over a yellow canvas the transparent corner shows yellow; the photo itself stays red.
        using var over = new MagickImage(new MagickColor(255, 255, 0), image.Width, image.Height);
        over.Composite(image, 0, 0, CompositeOperator.SrcOver);
        AssertCrop(over, 0, 0, new MagickColor(255, 255, 0));
        AssertCrop(over, 70, 70, Red);
    }

    [Fact]
    public void A_second_save_gets_a_numbered_name_and_never_overwrites()
    {
        var first = OutputPaths.Unique(_directory, "photo With Background", "png");
        Assert.Equal(Path.Combine(_directory, "photo With Background.png"), first);
        File.WriteAllBytes(first, new byte[] { 0 });

        var second = OutputPaths.Unique(_directory, "photo With Background", "png");
        Assert.Equal(Path.Combine(_directory, "photo With Background (2).png"), second);
        File.WriteAllBytes(second, new byte[] { 1 });

        var third = OutputPaths.Unique(_directory, "photo With Background", "png");
        Assert.Equal(Path.Combine(_directory, "photo With Background (3).png"), third);
        Assert.False(File.Exists(third));
    }

    private string Solid(string name, int width, int height, MagickColor color)
    {
        var path = Path.Combine(_directory, name);
        using var image = new MagickImage(color, (uint)width, (uint)height);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private string Render(string source, BackgroundFill fill, int padding, int corners, int shadow, BackgroundRatio ratio)
    {
        var target = Path.Combine(_directory, $"out-{Guid.NewGuid():N}.png");
        AddBackgroundRenderer.Render(source, fill, padding, corners, shadow, ratio, target);
        return target;
    }

    private static void AssertPixel(string file, int x, int y, MagickColor expected)
    {
        using var image = new MagickImage(file);
        AssertCrop(image, x, y, expected);
    }

    private static void AssertCrop(MagickImage image, int x, int y, MagickColor expected)
    {
        using var probe = new MagickImage(image);
        probe.Crop(new MagickGeometry(x, y, 1, 1));
        probe.ResetPage();
        using var want = new MagickImage(expected, 1, 1);
        Assert.Equal(0.0, probe.Compare(want, ErrorMetric.Absolute), 6);
    }
}
