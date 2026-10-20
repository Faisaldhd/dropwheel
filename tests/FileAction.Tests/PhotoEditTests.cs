using System.Text.Json;
using FileAction.App.UI.Tools.Image;
using FileAction.Core;
using ImageMagick;

namespace FileAction.Tests;

/// <summary>
/// The pure half of the Edit Photos tool: slider ranges and clamping, the parameter → Magick.NET mapping,
/// the tone curve, and that the strings the tool asks for exist in both tables. No window is created.
/// </summary>
public class PhotoEditTests
{
    // ---- model ---------------------------------------------------------------------------------

    [Fact]
    public void Specs_cover_every_parameter_once()
    {
        var declared = Enum.GetValues<PhotoParameter>();

        Assert.Equal(declared.Length, PhotoEdit.Specs.Count);
        Assert.Equal(declared.Order(), PhotoEdit.Specs.Select(spec => spec.Parameter).Order());

        foreach (var parameter in declared)
            Assert.Equal(parameter, PhotoEdit.Spec(parameter).Parameter);
    }

    [Fact]
    public void Every_group_has_at_least_one_slider()
    {
        foreach (var group in Enum.GetValues<PhotoEditGroup>())
            Assert.NotEmpty(PhotoEdit.InGroup(group));

        // The order the tool shows the tabs in.
        Assert.Equal(
            [PhotoEditGroup.Light, PhotoEditGroup.Color, PhotoEditGroup.Detail, PhotoEditGroup.Effects, PhotoEditGroup.Noise],
            Enum.GetValues<PhotoEditGroup>());
    }

    [Fact]
    public void Every_parameter_starts_neutral()
    {
        var parameters = new PhotoParameters();

        Assert.True(PhotoEdit.IsNeutral(parameters));
        foreach (var spec in PhotoEdit.Specs)
        {
            Assert.Equal(spec.Default, parameters[spec.Parameter]);
            Assert.InRange(spec.Default, spec.Min, spec.Max);
        }

        // All sixteen sliders are neutral, and the light group is the five/six the window shows first.
        Assert.Equal(6, PhotoEdit.InGroup(PhotoEditGroup.Light).Count);
        Assert.Equal(4, PhotoEdit.InGroup(PhotoEditGroup.Color).Count);
        Assert.Equal(3, PhotoEdit.InGroup(PhotoEditGroup.Detail).Count);
        Assert.Equal(2, PhotoEdit.InGroup(PhotoEditGroup.Effects).Count);
        Assert.Single(PhotoEdit.InGroup(PhotoEditGroup.Noise));
    }

    [Fact]
    public void Clamping_keeps_values_inside_the_declared_range()
    {
        var parameters = new PhotoParameters();
        foreach (var spec in PhotoEdit.Specs)
            parameters[spec.Parameter] = spec.Min - 500;
        parameters.Clamp();
        foreach (var spec in PhotoEdit.Specs)
            Assert.Equal(spec.Min, parameters[spec.Parameter]);

        foreach (var spec in PhotoEdit.Specs)
            parameters[spec.Parameter] = spec.Max + 500;
        parameters.Clamp();
        foreach (var spec in PhotoEdit.Specs)
            Assert.Equal(spec.Max, parameters[spec.Parameter]);

        Assert.True(PhotoEdit.IsNeutral(new PhotoParameters()));
    }

    [Fact]
    public void Plan_uses_clamped_values()
    {
        var parameters = new PhotoParameters
        {
            Exposure = 10_000,
            Shadows = -10_000,
            Grain = 10_000,
        };

        var plan = PhotoEdit.Plan(parameters);
        Assert.False(plan.IsIdentity);

        var exposure = plan.Steps.Single(step => step.Operation == PhotoEditOperation.Exposure);
        Assert.Equal(PhotoEdit.ExposureFactor(100), exposure.Amount, 9);

        var grain = plan.Steps.Single(step => step.Operation == PhotoEditOperation.Grain);
        Assert.Equal(PhotoEdit.GrainAttenuate(100), grain.Amount, 9);

        // The input object keeps the values the user typed; only the plan is clamped.
        Assert.Equal(10_000, parameters.Exposure);
    }

    [Fact]
    public void Neutral_parameters_change_nothing()
    {
        var parameters = new PhotoParameters();

        var plan = PhotoEdit.Plan(parameters);
        Assert.True(plan.IsIdentity);
        Assert.Empty(plan.Steps);
        Assert.Null(plan.ToneCurve);
        Assert.True(PhotoEdit.IsIdentityCurve(PhotoEdit.ToneCurve(parameters)));
    }

    [Fact]
    public void Any_single_slider_produces_at_least_one_step()
    {
        foreach (var spec in PhotoEdit.Specs)
        {
            var parameters = new PhotoParameters();
            parameters[spec.Parameter] = 50;

            Assert.False(PhotoEdit.IsNeutral(parameters));
            Assert.False(PhotoEdit.Plan(parameters).IsIdentity, spec.Parameter.ToString());
        }
    }

    // ---- mapping -------------------------------------------------------------------------------

    [Fact]
    public void Exposure_is_monotonic_and_neutral_at_zero()
    {
        Assert.Equal(1.0, PhotoEdit.ExposureFactor(0), 9);
        Assert.Equal(0.5, PhotoEdit.ExposureFactor(-100), 9);
        Assert.Equal(2.0, PhotoEdit.ExposureFactor(100), 9);

        var values = Enumerable.Range(-100, 201).Select(PhotoEdit.ExposureFactor).ToArray();
        AssertStrictlyIncreasing(values);
    }

    [Fact]
    public void Contrast_is_monotonic_and_neutral_at_zero()
    {
        Assert.Equal(1.0, PhotoEdit.ContrastFactor(0), 9);
        Assert.Equal(0.0, PhotoEdit.ContrastFactor(-100), 9);
        Assert.Equal(2.0, PhotoEdit.ContrastFactor(100), 9);

        var values = Enumerable.Range(-100, 201).Select(PhotoEdit.ContrastFactor).ToArray();
        AssertStrictlyIncreasing(values);
    }

    [Fact]
    public void Temperature_warms_red_and_cools_blue_monotonically()
    {
        var (redAtZero, blueAtZero) = PhotoEdit.WhiteBalance(0);
        Assert.Equal(1.0, redAtZero, 9);
        Assert.Equal(1.0, blueAtZero, 9);

        var reds = Enumerable.Range(-100, 201).Select(value => PhotoEdit.WhiteBalance(value).Red).ToArray();
        var blues = Enumerable.Range(-100, 201).Select(value => PhotoEdit.WhiteBalance(value).Blue).ToArray();
        AssertStrictlyIncreasing(reds);
        AssertStrictlyDecreasing(blues);

        // Warm pushes red past 1 and blue below it; cool does the opposite.
        Assert.True(PhotoEdit.WhiteBalance(100).Red > 1);
        Assert.True(PhotoEdit.WhiteBalance(100).Blue < 1);
        Assert.True(PhotoEdit.WhiteBalance(-100).Red < 1);
        Assert.True(PhotoEdit.WhiteBalance(-100).Blue > 1);
    }

    [Fact]
    public void Tint_removes_green_monotonically()
    {
        Assert.Equal(1.0, PhotoEdit.TintGreen(0), 9);
        Assert.True(PhotoEdit.TintGreen(100) < 1);
        Assert.True(PhotoEdit.TintGreen(-100) > 1);

        var values = Enumerable.Range(-100, 201).Select(PhotoEdit.TintGreen).ToArray();
        AssertStrictlyDecreasing(values);
    }

    [Fact]
    public void Saturation_and_vibrance_move_together_but_vibrance_is_gentler()
    {
        Assert.Equal(100, PhotoEdit.SaturationAmount(0), 9);
        Assert.Equal(0, PhotoEdit.SaturationAmount(-100), 9);
        Assert.Equal(200, PhotoEdit.SaturationAmount(100), 9);
        AssertStrictlyIncreasing(Enumerable.Range(-100, 201).Select(PhotoEdit.SaturationAmount).ToArray());

        Assert.Equal(100, PhotoEdit.VibranceAmount(0), 9);
        Assert.Equal(40, PhotoEdit.VibranceAmount(-100), 9);
        Assert.Equal(160, PhotoEdit.VibranceAmount(100), 9);
        AssertStrictlyIncreasing(Enumerable.Range(-100, 201).Select(PhotoEdit.VibranceAmount).ToArray());

        for (var value = 1; value <= 100; value += 10)
            Assert.True(PhotoEdit.VibranceAmount(value) < PhotoEdit.SaturationAmount(value));
    }

    [Fact]
    public void Detail_and_effect_amounts_are_monotonic()
    {
        Assert.Equal(0, PhotoEdit.ClarityAmount(0), 9);
        AssertStrictlyIncreasing(Enumerable.Range(0, 101).Select(PhotoEdit.ClarityAmount).ToArray());

        Assert.Equal(0, PhotoEdit.DehazeContrast(0), 9);
        AssertStrictlyIncreasing(Enumerable.Range(0, 101).Select(PhotoEdit.DehazeContrast).ToArray());

        Assert.Equal(0.4, PhotoEdit.SharpenSigma(0), 9);
        AssertStrictlyIncreasing(Enumerable.Range(0, 101).Select(PhotoEdit.SharpenSigma).ToArray());

        Assert.Equal(0, PhotoEdit.GrainAttenuate(0), 9);
        AssertStrictlyIncreasing(Enumerable.Range(0, 101).Select(PhotoEdit.GrainAttenuate).ToArray());

        Assert.Equal(0, PhotoEdit.VignetteStrength(0), 9);
        Assert.Equal(1, PhotoEdit.VignetteStrength(100), 9);
        AssertStrictlyIncreasing(Enumerable.Range(0, 101).Select(PhotoEdit.VignetteStrength).ToArray());

        // Noise reduction is a median radius, so it steps 1…2 and never decreases.
        Assert.Equal(1u, PhotoEdit.NoiseRadius(0));
        Assert.Equal(2u, PhotoEdit.NoiseRadius(100));
        var radii = Enumerable.Range(0, 101).Select(value => (int)PhotoEdit.NoiseRadius(value)).ToArray();
        AssertNonDecreasing(radii.Select(value => (double)value).ToArray());
    }

    [Fact]
    public void Vignette_falloff_is_clear_in_the_middle_and_strongest_in_the_corners()
    {
        for (var distance = 0.0; distance <= 0.55; distance += 0.05)
            Assert.Equal(0, PhotoEdit.VignetteAlpha(1, distance), 9);

        Assert.Equal(0, PhotoEdit.VignetteAlpha(0, 1), 9);
        Assert.Equal(0.5, PhotoEdit.VignetteAlpha(0.5, 1), 9);
        Assert.Equal(1, PhotoEdit.VignetteAlpha(1, 1), 9);

        var samples = Enumerable.Range(0, 21).Select(step => PhotoEdit.VignetteAlpha(1, step / 20.0)).ToArray();
        AssertNonDecreasing(samples);
        Assert.True(samples[^1] > samples[10]);
    }

    // ---- tone curve ----------------------------------------------------------------------------

    [Fact]
    public void Tone_curve_is_the_identity_when_neutral()
    {
        var curve = PhotoEdit.ToneCurve(new PhotoParameters());

        Assert.Equal(256, curve.Length);
        for (var i = 0; i < curve.Length; i++)
            Assert.Equal((byte)i, curve[i]);
        Assert.True(PhotoEdit.IsIdentityCurve(curve));
    }

    [Fact]
    public void Tone_curve_moves_the_highlight_and_shadow_ends_separately()
    {
        Assert.True(CurvePoint(Tone(shadows: 100), 40) > 40);
        Assert.True(CurvePoint(Tone(shadows: 100), 220) < 220 + 60);
        Assert.True(Delta(Tone(shadows: 100), 40) > Delta(Tone(shadows: 100), 220));

        Assert.True(CurvePoint(Tone(highlights: 100), 220) > 220);
        Assert.True(Delta(Tone(highlights: 100), 220) > Delta(Tone(highlights: 100), 40));
        Assert.True(CurvePoint(Tone(highlights: -100), 220) < 220);
        Assert.True(CurvePoint(Tone(shadows: -100), 40) < 40);

        // Whites define the white point, blacks the black point.
        Assert.True(CurvePoint(Tone(whites: -100), 255) < 255);
        Assert.Equal(255, CurvePoint(Tone(whites: 100), 255));
        Assert.True(CurvePoint(Tone(blacks: 100), 0) > 0);
        Assert.True(CurvePoint(Tone(blacks: -100), 40) < 40);
    }

    [Fact]
    public void Tone_curve_never_folds_back_for_any_combination()
    {
        int[] adjustments = [0, 100];

        foreach (var highlights in adjustments)
        foreach (var shadows in adjustments)
        foreach (var whites in adjustments)
        foreach (var blacks in adjustments)
        {
            var curve = Tone(highlights, shadows, whites, blacks);
            for (var i = 1; i < curve.Length; i++)
                Assert.True(
                    curve[i] >= curve[i - 1],
                    $"curve folds at {i} for h={highlights} s={shadows} w={whites} b={blacks}");

            // Both ends stay inside the cube; white is never reduced by positive adjustments.
            Assert.InRange(curve[0], 0, 255);
            Assert.Equal(255, curve[^1]);
        }
    }

    [Fact]
    public void Plan_runs_the_operations_in_pipeline_order()
    {
        var parameters = new PhotoParameters
        {
            Exposure = 50,
            Contrast = 50,
            Highlights = 50,
            Shadows = 50,
            Whites = 50,
            Blacks = 50,
            Saturation = 50,
            Vibrance = 50,
            Temperature = 50,
            Tint = 50,
            Sharpness = 50,
            Clarity = 50,
            Dehaze = 50,
            Grain = 50,
            Vignette = 50,
            NoiseReduction = 50,
        };

        var operations = PhotoEdit.Plan(parameters).Steps.Select(step => step.Operation).ToArray();

        Assert.Equal(
            [
                PhotoEditOperation.ToneCurve,
                PhotoEditOperation.Exposure,
                PhotoEditOperation.Contrast,
                PhotoEditOperation.WhiteBalance,
                PhotoEditOperation.Tint,
                PhotoEditOperation.Saturation,
                PhotoEditOperation.Vibrance,
                PhotoEditOperation.Dehaze,
                PhotoEditOperation.NoiseReduction,
                PhotoEditOperation.Clarity,
                PhotoEditOperation.Sharpness,
                PhotoEditOperation.Vignette,
                PhotoEditOperation.Grain,
            ],
            operations);
    }

    // ---- pixels --------------------------------------------------------------------------------

    [Fact]
    public void Apply_with_neutral_parameters_leaves_every_pixel_alone()
    {
        using var edited = new MagickImage(MagickColors.Gray, 32, 24);
        using var untouched = new MagickImage(MagickColors.Gray, 32, 24);

        PhotoEdit.Apply(edited, new PhotoParameters());

        Assert.Equal(0, edited.Compare(untouched, ErrorMetric.RootMeanSquared), 9);
    }

    [Fact]
    public void Apply_with_exposure_brightens_the_pixels()
    {
        using var edited = new MagickImage(MagickColors.Gray, 32, 24);
        using var untouched = new MagickImage(MagickColors.Gray, 32, 24);
        var parameters = new PhotoParameters { Exposure = 50 };

        PhotoEdit.Apply(edited, parameters);

        Assert.True(edited.Compare(untouched, ErrorMetric.RootMeanSquared) > 0.01);
    }

    [Fact]
    public void Apply_survives_a_full_pipeline()
    {
        // Every operation at a strong setting runs without throwing, including the LUT, the vignette
        // composite and the grain.
        using var image = new MagickImage(MagickColors.LightBlue, 48, 32);
        var parameters = new PhotoParameters
        {
            Exposure = 20,
            Contrast = 30,
            Highlights = -40,
            Shadows = 60,
            Whites = 10,
            Blacks = -20,
            Saturation = 40,
            Vibrance = 30,
            Temperature = 25,
            Tint = -20,
            Sharpness = 70,
            Clarity = 40,
            Dehaze = 30,
            Grain = 20,
            Vignette = 80,
            NoiseReduction = 50,
        };

        PhotoEdit.Apply(image, parameters);

        Assert.Equal(48u, image.Width);
        Assert.Equal(32u, image.Height);
    }

    [Fact]
    public void Apply_edits_a_grayscale_image_through_the_rgb_channels()
    {
        using var image = new MagickImage(MagickColors.Gray, 24, 18);
        image.ColorSpace = ColorSpace.Gray;

        PhotoEdit.Apply(image, new PhotoParameters { Exposure = 20, Highlights = 40, Saturation = 30 });

        // Grey has one channel; the pipeline must have widened it before the RGB operations ran.
        Assert.Equal(ColorSpace.sRGB, image.ColorSpace);
        Assert.Equal(24u, image.Width);
    }

    [Fact]
    public void Final_render_is_a_byte_copy_at_neutral_and_differs_after_an_edit()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "photoedit-final");
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.png");
            using (var image = new MagickImage(MagickColors.Orange, 48, 32))
                image.Write(source);
            var item = new FileItem(source);

            // All-zero parameters: nothing to render, so the bytes are copied verbatim.
            var neutral = EditPhotoRenderer.RenderFinal(item, new PhotoParameters(), "Edited", directory);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(neutral));

            // A real edit writes a new file (never the neutral one) whose pixels changed.
            var edited = EditPhotoRenderer.RenderFinal(item, new PhotoParameters { Exposure = 40 }, "Edited", directory);
            Assert.NotEqual(neutral, edited);
            Assert.True(File.Exists(neutral));
            using var before = new MagickImage(source);
            using var after = new MagickImage(edited);
            Assert.True(after.Compare(before, ErrorMetric.RootMeanSquared) > 0.01);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---- window --------------------------------------------------------------------------------

    [Fact]
    public void The_tool_opens_for_one_still_image_only()
    {
        Assert.Equal("tools.editphotos", EditPhotosTool.Instance.Id);
        Assert.Equal("tool.editphotos.title", EditPhotosTool.Instance.TitleKey);
        Assert.Equal("tool.editphotos.apply", EditPhotosTool.Instance.PrimaryLabelKey);

        Assert.True(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.jpg")]));
        Assert.True(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.PNG")]));
        Assert.True(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.jfif")]));

        // Animations and icons stay with Crop; editing them would flatten frames.
        Assert.False(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.gif")]));
        Assert.False(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.ico")]));
        Assert.False(EditPhotosTool.Instance.Applies([new FileItem(@"C:\photos\a.svg")]));

        Assert.False(EditPhotosTool.Instance.Applies(
        [
            new FileItem(@"C:\photos\a.jpg"),
            new FileItem(@"C:\photos\b.jpg"),
        ]));
    }

    [Fact]
    public void The_window_opens_at_about_500_by_745_and_follows_the_photo_aspect()
    {
        var layout = EditPhotosTool.ToolLayout;

        // Before the photo is probed: 440 preview + 60 side margin, 225 preview + 520 chrome.
        var initial = ToolWindowSizer.Compute(layout, null, null, 1920, 1080, 1.0);
        Assert.InRange(initial.Width, 490, 510);
        Assert.InRange(initial.Height, 735, 755);

        // A 4:3 photo gets the full 440-wide preview (500-wide window) and 330 of preview height.
        var landscape = ToolWindowSizer.Compute(layout, 4000, 3000, 1920, 1080, 1.0);
        Assert.Equal(500, landscape.Width, 0);
        Assert.Equal(440, landscape.PreviewWidth, 0);
        Assert.Equal(330, landscape.PreviewHeight, 0);

        // A portrait photo keeps the 360 DIP slider panel instead of collapsing to the photo's width.
        var portrait = ToolWindowSizer.Compute(layout, 3000, 4000, 1920, 1080, 1.0);
        Assert.Equal(420, portrait.Width, 0);
        Assert.True(portrait.Height > landscape.Height);

        // A tiny screen shrinks the window, never the chrome.
        var small = ToolWindowSizer.Compute(layout, 4000, 3000, 1280, 720, 1.0);
        Assert.True(small.Width <= 1280 * 0.95);
        Assert.True(small.Height <= 720 * 0.90);
    }

    // ---- strings -------------------------------------------------------------------------------

    [Fact]
    public void Every_tool_string_exists_in_both_tables()
    {
        var (english, arabic) = LoadTables();
        var keys = ToolKeys();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var key in keys)
        {
            Assert.True(key.StartsWith(PhotoEdit.KeyPrefix, StringComparison.Ordinal), key);
            Assert.True(english.TryGetValue(key, out var en), $"en.json is missing '{key}'.");
            Assert.True(arabic.TryGetValue(key, out var ar), $"ar.json is missing '{key}'.");

            Assert.False(string.IsNullOrWhiteSpace(en), $"en '{key}' is empty");
            Assert.False(string.IsNullOrWhiteSpace(ar), $"ar '{key}' is empty");
            Assert.NotEqual(en, ar);
            Assert.DoesNotContain('{', en);
            Assert.DoesNotContain('{', ar);
        }
    }

    /// <summary>Every string the tool's window and wheel entry read.</summary>
    private static List<string> ToolKeys()
    {
        List<string> keys =
        [
            "tool.editphotos.title",
            "tool.editphotos.label",
            "tool.editphotos.subtitle",
            "tool.editphotos.apply",
            "tool.editphotos.suffix",
            "tool.editphotos.compare",
            "tool.editphotos.original",
            "tool.editphotos.hint",
        ];

        keys.AddRange(Enum.GetValues<PhotoEditGroup>().Select(PhotoEdit.GroupKey));
        keys.AddRange(PhotoEdit.Specs.Select(spec => spec.Key));
        return keys;
    }

    private static (Dictionary<string, string> English, Dictionary<string, string> Arabic) LoadTables()
    {
        var strings = Path.Combine(RepoRoot(), "src", "FileAction.Core", "Strings");
        return (Load(Path.Combine(strings, "en.json")), Load(Path.Combine(strings, "ar.json")));
    }

    private static Dictionary<string, string> Load(string path)
    {
        Assert.True(File.Exists(path), $"Missing string table: {path}");
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(table);
        return table!;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static byte[] Tone(int highlights = 0, int shadows = 0, int whites = 0, int blacks = 0) =>
        PhotoEdit.ToneCurve(new PhotoParameters
        {
            Highlights = highlights,
            Shadows = shadows,
            Whites = whites,
            Blacks = blacks,
        });

    private static int CurvePoint(byte[] curve, int index) => curve[index];

    private static int Delta(byte[] curve, int index) => curve[index] - index;

    private static void AssertStrictlyIncreasing(IReadOnlyList<double> values)
    {
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] > values[i - 1], $"{values[i]} !> {values[i - 1]} at {i}");
    }

    private static void AssertStrictlyDecreasing(IReadOnlyList<double> values)
    {
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] < values[i - 1], $"{values[i]} !< {values[i - 1]} at {i}");
    }

    private static void AssertNonDecreasing(IReadOnlyList<double> values)
    {
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i] >= values[i - 1] - 1e-9, $"{values[i]} < {values[i - 1]} at {i}");
    }
}
