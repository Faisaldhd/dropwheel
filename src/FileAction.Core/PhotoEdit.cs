using ImageMagick;

namespace FileAction.Core;

/// <summary>The tab of the Edit Photos tool a slider belongs to (see <see cref="PhotoEdit.Specs"/>).</summary>
public enum PhotoEditGroup
{
    Light,
    Color,
    Detail,
    Effects,
    Noise,
}

/// <summary>Every slider the tool offers, in the order the tool declares them.</summary>
public enum PhotoParameter
{
    Exposure,
    Contrast,
    Highlights,
    Shadows,
    Whites,
    Blacks,
    Saturation,
    Vibrance,
    Temperature,
    Tint,
    Sharpness,
    Clarity,
    Dehaze,
    Grain,
    Vignette,
    NoiseReduction,
}

/// <summary>What one slider may be set to. Values are integers so a slider position is exact.</summary>
/// <param name="Parameter">The parameter this row edits.</param>
/// <param name="Group">The tab the row is shown on.</param>
/// <param name="Min">Lowest accepted value; the default is <see cref="Default"/>.</param>
/// <param name="Max">Highest accepted value.</param>
public readonly record struct PhotoParameterSpec(PhotoParameter Parameter, PhotoEditGroup Group, int Min, int Max)
{
    /// <summary>The neutral position of the slider; every parameter starts there.</summary>
    public int Default => 0;

    /// <summary>True when the slider has a meaningful negative side (a centre-based track).</summary>
    public bool IsBipolar => Min < 0;

    /// <summary>Localization key of the slider's label.</summary>
    public string Key => PhotoEdit.KeyOf(Parameter);

    /// <summary>The value clamped into the declared range.</summary>
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
}

/// <summary>One Magick.NET operation the edit pipeline performs, with the exact arguments it gets.</summary>
public enum PhotoEditOperation
{
    /// <summary>The tone curve built from highlights/shadows/whites/blacks (see <see cref="PhotoEdit.ToneCurve"/>).</summary>
    ToneCurve,
    Exposure,
    Contrast,
    WhiteBalance,
    Tint,
    Saturation,
    Vibrance,
    Dehaze,
    NoiseReduction,
    Clarity,
    Sharpness,
    Vignette,
    Grain,
}

/// <summary>
/// One step of the edit pipeline: which operation and the scalar arguments handed to the Magick.NET call.
/// </summary>
/// <param name="Operation">The operation to run.</param>
/// <param name="Amount">First argument of the call (factor, percentage, sigma, radius, strength…).</param>
/// <param name="Secondary">
/// Second argument, used only by <see cref="PhotoEditOperation.WhiteBalance"/> for the blue gain.
/// </param>
public readonly record struct PhotoEditStep(PhotoEditOperation Operation, double Amount, double Secondary = 0);

/// <summary>
/// A fully resolved edit: the ordered operations plus the tone curve the first step applies. Empty and
/// curve-less means "the parameters are neutral, touching the image would be wrong".
/// </summary>
public sealed record PhotoEditPlan(IReadOnlyList<PhotoEditStep> Steps, byte[]? ToneCurve)
{
    public static readonly PhotoEditPlan Identity = new([], null);

    /// <summary>True when this plan would not change a single pixel.</summary>
    public bool IsIdentity => Steps.Count == 0 && ToneCurve is null;
}

/// <summary>
/// The slider values of the Edit Photos tool: ranges, defaults, clamping, and the exact mapping from those
/// values to the Magick.NET operations — kept free of windows and file IO so every part is unit tested.
///
/// The preview and the final render call the same <see cref="Apply"/>, so what the user sees is what is
/// written to the new file.
/// </summary>
public static class PhotoEdit
{
    /// <summary>Prefix shared by every string the tool uses.</summary>
    public const string KeyPrefix = "tool.editphotos.";

    /// <summary>The sliders, in declaration order (Light, Color, Detail, Effects, Noise).</summary>
    public static readonly IReadOnlyList<PhotoParameterSpec> Specs =
    [
        new(PhotoParameter.Exposure, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Contrast, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Highlights, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Shadows, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Whites, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Blacks, PhotoEditGroup.Light, -100, 100),
        new(PhotoParameter.Saturation, PhotoEditGroup.Color, -100, 100),
        new(PhotoParameter.Vibrance, PhotoEditGroup.Color, -100, 100),
        new(PhotoParameter.Temperature, PhotoEditGroup.Color, -100, 100),
        new(PhotoParameter.Tint, PhotoEditGroup.Color, -100, 100),
        new(PhotoParameter.Sharpness, PhotoEditGroup.Detail, 0, 100),
        new(PhotoParameter.Clarity, PhotoEditGroup.Detail, 0, 100),
        new(PhotoParameter.Dehaze, PhotoEditGroup.Detail, 0, 100),
        new(PhotoParameter.Grain, PhotoEditGroup.Effects, 0, 100),
        new(PhotoParameter.Vignette, PhotoEditGroup.Effects, 0, 100),
        new(PhotoParameter.NoiseReduction, PhotoEditGroup.Noise, 0, 100),
    ];

    private static readonly PhotoParameterSpec[] ByParameter = Index();

    /// <summary>Largest side of the intermediate preview; the live preview never edits full resolution.</summary>
    public const int PreviewMaxSide = 1200;

    /// <summary>Highest side of the vignette mask; it is resized up instead of looping over every pixel.</summary>
    private const int VignetteMaskMaxSide = 512;

    /// <summary>X positions of the five tone-curve knots (black, lower mid, mid, upper mid, white).</summary>
    private static readonly double[] CurveKnots = [0.0, 0.25, 0.5, 0.75, 1.0];

    /// <summary>The spec of one parameter.</summary>
    public static PhotoParameterSpec Spec(PhotoParameter parameter) => ByParameter[(int)parameter];

    /// <summary>The sliders of one tab, in declaration order.</summary>
    public static IReadOnlyList<PhotoParameterSpec> InGroup(PhotoEditGroup group) =>
        [.. Specs.Where(spec => spec.Group == group)];

    /// <summary>Localization key of a slider label, e.g. <c>tool.editphotos.noiseReduction</c>.</summary>
    public static string KeyOf(PhotoParameter parameter) =>
        KeyPrefix + char.ToLowerInvariant(parameter.ToString()[0]) + parameter.ToString()[1..];

    /// <summary>Localization key of a tab label, e.g. <c>tool.editphotos.group.light</c>.</summary>
    public static string GroupKey(PhotoEditGroup group) => KeyPrefix + "group." + group.ToString().ToLowerInvariant();

    /// <summary>True when every slider sits at its default, i.e. editing would be a copy.</summary>
    public static bool IsNeutral(PhotoParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        foreach (var spec in Specs)
            if (parameters[spec.Parameter] != spec.Default)
                return false;
        return true;
    }

    // ---- Mapping ------------------------------------------------------------------------------
    // Every function below is the single source of the number Magick.NET is called with, so the tests
    // can pin the mapping (monotonic, neutral at zero) without decoding pixel values.

    /// <summary>Exposure as a multiplier: 2^(-1)=0.5 at -100, 1 at 0, 2 at +100 (one stop per 100).</summary>
    public static double ExposureFactor(int exposure) => Math.Pow(2, Clamp(exposure, -100, 100) / 100.0);

    /// <summary>Contrast as the slope of a straight line through 50% grey: 0 at -100, 1 at 0, 2 at +100.</summary>
    public static double ContrastFactor(int contrast) => 1 + Clamp(contrast, -100, 100) / 100.0;

    /// <summary>Red and blue gains for the temperature slider: warm raises red, cools blue.</summary>
    public static (double Red, double Blue) WhiteBalance(int temperature)
    {
        var value = Clamp(temperature, -100, 100) / 100.0;
        return (1 + 0.30 * value, 1 - 0.25 * value);
    }

    /// <summary>Green gain for the tint slider: positive is magenta (less green).</summary>
    public static double TintGreen(int tint) => 1 - 0.25 * Clamp(tint, -100, 100) / 100.0;

    /// <summary>Saturation percentage for Magick's modulate: 0 at -100, 100 at 0, 200 at +100.</summary>
    public static double SaturationAmount(int saturation) => 100 + Clamp(saturation, -100, 100);

    /// <summary>Vibrance uses the same scale but a gentler factor, so skin tones move less.</summary>
    public static double VibranceAmount(int vibrance) => 100 + 0.6 * Clamp(vibrance, -100, 100);

    /// <summary>Dehaze as a sigmoidal contrast amount: 0 at 0, 2.5 at 100.</summary>
    public static double DehazeContrast(int dehaze) => 2.5 * Clamp(dehaze, 0, 100) / 100.0;

    /// <summary>Median-filter radius for noise reduction: 1 (no-op radius kept honest) to 4.</summary>
    public static uint NoiseRadius(int noiseReduction) =>
        (uint)Math.Clamp(1 + (int)Math.Round(Clamp(noiseReduction, 0, 100) / 100.0 * 3), 1, 4);

    /// <summary>Unsharp amount for clarity: local contrast, independent of the fine sharpening.</summary>
    public static double ClarityAmount(int clarity) => 1.7 * Clamp(clarity, 0, 100) / 100.0;

    /// <summary>Sharpening sigma; the radius stays at one pixel so only fine detail is affected.</summary>
    public static double SharpenSigma(int sharpness) => 0.4 + 1.1 * Clamp(sharpness, 0, 100) / 100.0;

    /// <summary>Grain attenuation handed to <c>AddNoise</c>: 0 to 0.2.</summary>
    public static double GrainAttenuate(int grain) => 0.20 * Clamp(grain, 0, 100) / 100.0;

    /// <summary>Vignette strength, 0 to 1.</summary>
    public static double VignetteStrength(int vignette) => Clamp(vignette, 0, 100) / 100.0;

    /// <summary>
    /// Vignette alpha at a point: 0 inside the middle half of the frame, growing smoothly to the full
    /// strength at the corners. Pure, so the falloff shape is pinned by a test.
    /// </summary>
    /// <param name="strength">0–1, from <see cref="VignetteStrength"/>.</param>
    /// <param name="distance">0 at the centre, 1 at the corners.</param>
    public static double VignetteAlpha(double strength, double distance)
    {
        var amount = Math.Clamp(strength, 0, 1);
        if (amount <= 0 || distance <= 0.55)
            return 0;
        var t = Math.Clamp((distance - 0.55) / 0.45, 0, 1);
        var smooth = t * t * (3 - 2 * t);
        return amount * smooth;
    }

    // ---- Tone curve ---------------------------------------------------------------------------

    /// <summary>
    /// The 256-entry tone curve for highlights, shadows, whites and blacks, in one lookup table. The
    /// five knots are interpolated with a monotone cubic, so the curve never folds back on itself for any
    /// combination of parameters, and all-zero parameters give the exact identity (curve[i] == i).
    /// </summary>
    public static byte[] ToneCurve(PhotoParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var source = parameters.Normalized();
        var shadows = source.Shadows / 100.0;
        var highlights = source.Highlights / 100.0;
        var whites = source.Whites / 100.0;
        var blacks = source.Blacks / 100.0;

        // Lightroom-style direction: shadows/blacks lift the dark end, highlights/whites the bright one.
        var values = new double[CurveKnots.Length];
        values[0] = 0.00 + 0.40 * shadows + 0.18 * blacks;
        values[1] = 0.25 + 0.22 * shadows + 0.04 * blacks;
        values[2] = 0.50 + 0.02 * (shadows + highlights);
        values[3] = 0.75 + 0.22 * highlights + 0.04 * whites;
        values[4] = 1.00 + 0.40 * highlights + 0.18 * whites;

        // Keep the knots inside the cube and in order; the interpolation needs a function, not a loop.
        var previous = 0.0;
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Math.Clamp(values[i], 0, 1);
            values[i] = Math.Max(values[i], previous);
            previous = values[i];
        }

        var slopes = MonotoneSlopes(values);
        var curve = new byte[256];
        for (var i = 0; i < curve.Length; i++)
        {
            var y = Interpolate(values, slopes, i / 255.0);
            curve[i] = (byte)Math.Round(Math.Clamp(y, 0, 1) * 255, MidpointRounding.AwayFromZero);
        }
        return curve;
    }

    /// <summary>True when the curve is the identity, i.e. it would not change any pixel.</summary>
    public static bool IsIdentityCurve(byte[] curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Length != 256)
            return false;
        for (var i = 0; i < curve.Length; i++)
            if (curve[i] != i)
                return false;
        return true;
    }

    /// <summary>Fritsch–Carlson slopes: the cubic Hermite that cannot overshoot a monotone knot set.</summary>
    private static double[] MonotoneSlopes(double[] values)
    {
        var n = values.Length;
        var deltas = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            deltas[i] = (values[i + 1] - values[i]) / (CurveKnots[i + 1] - CurveKnots[i]);

        var slopes = new double[n];
        slopes[0] = deltas[0];
        slopes[n - 1] = deltas[n - 2];
        for (var i = 1; i < n - 1; i++)
            slopes[i] = (deltas[i - 1] + deltas[i]) / 2;

        for (var i = 0; i < n - 1; i++)
        {
            if (deltas[i] <= 0)
            {
                slopes[i] = 0;
                slopes[i + 1] = 0;
                continue;
            }

            var a = slopes[i] / deltas[i];
            var b = slopes[i + 1] / deltas[i];
            var sum = a * a + b * b;
            if (sum <= 9)
                continue;

            var scale = 3 / Math.Sqrt(sum);
            slopes[i] = scale * a * deltas[i];
            slopes[i + 1] = scale * b * deltas[i];
        }
        return slopes;
    }

    /// <summary>Cubic Hermite between the two knots around <paramref name="x"/>.</summary>
    private static double Interpolate(double[] values, double[] slopes, double x)
    {
        var last = CurveKnots.Length - 1;
        if (x <= CurveKnots[0])
            return values[0];
        if (x >= CurveKnots[last])
            return values[last];

        var index = 0;
        while (index < last - 1 && x > CurveKnots[index + 1])
            index++;

        var width = CurveKnots[index + 1] - CurveKnots[index];
        var t = (x - CurveKnots[index]) / width;
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 = t3 - t2;
        return h00 * values[index]
            + h10 * width * slopes[index]
            + h01 * values[index + 1]
            + h11 * width * slopes[index + 1];
    }

    // ---- Pipeline -----------------------------------------------------------------------------

    /// <summary>
    /// Resolves the parameters into the ordered operations <see cref="Apply"/> runs. The order is the
    /// pipeline order: tone first, then light, colour, detail, noise and finally grain over everything.
    /// </summary>
    public static PhotoEditPlan Plan(PhotoParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var source = parameters.Normalized();
        var curve = ToneCurve(source);
        var hasCurve = !IsIdentityCurve(curve);

        var steps = new List<PhotoEditStep>(12);
        if (hasCurve)
            steps.Add(new PhotoEditStep(PhotoEditOperation.ToneCurve, 0));
        if (source.Exposure != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Exposure, ExposureFactor(source.Exposure)));
        if (source.Contrast != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Contrast, ContrastFactor(source.Contrast)));
        if (source.Temperature != 0)
        {
            var (red, blue) = WhiteBalance(source.Temperature);
            steps.Add(new PhotoEditStep(PhotoEditOperation.WhiteBalance, red, blue));
        }
        if (source.Tint != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Tint, TintGreen(source.Tint)));
        if (source.Saturation != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Saturation, SaturationAmount(source.Saturation)));
        if (source.Vibrance != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Vibrance, VibranceAmount(source.Vibrance)));
        if (source.Dehaze != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Dehaze, DehazeContrast(source.Dehaze)));
        if (source.NoiseReduction != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.NoiseReduction, NoiseRadius(source.NoiseReduction)));
        if (source.Clarity != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Clarity, ClarityAmount(source.Clarity)));
        if (source.Sharpness != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Sharpness, SharpenSigma(source.Sharpness)));
        if (source.Vignette != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Vignette, VignetteStrength(source.Vignette)));
        if (source.Grain != 0)
            steps.Add(new PhotoEditStep(PhotoEditOperation.Grain, GrainAttenuate(source.Grain)));

        return steps.Count == 0 && !hasCurve ? PhotoEditPlan.Identity : new PhotoEditPlan(steps, hasCurve ? curve : null);
    }

    /// <summary>
    /// Applies the parameters to <paramref name="image"/> in place. The preview and the saved file both
    /// call this, the preview on a downsampled copy and the final render on the original.
    /// </summary>
    public static void Apply(MagickImage image, PhotoParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(image);
        var plan = Plan(parameters);
        if (plan.IsIdentity)
            return;

        NormalizeChannels(image);

        foreach (var step in plan.Steps)
        {
            switch (step.Operation)
            {
                case PhotoEditOperation.ToneCurve:
                    if (plan.ToneCurve is { } curve)
                        ApplyToneCurve(image, curve);
                    break;
                case PhotoEditOperation.Exposure:
                    image.Evaluate(Channels.RGB, EvaluateOperator.Multiply, step.Amount);
                    break;
                case PhotoEditOperation.Contrast:
                    // A straight line through 50% grey: c0 + c1*x, so the slope is the contrast factor.
                    image.Evaluate(Channels.RGB, EvaluateFunction.Polynomial,
                        [0.5 - 0.5 * step.Amount, step.Amount]);
                    break;
                case PhotoEditOperation.WhiteBalance:
                    image.Evaluate(Channels.Red, EvaluateOperator.Multiply, step.Amount);
                    image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, step.Secondary);
                    break;
                case PhotoEditOperation.Tint:
                    image.Evaluate(Channels.Green, EvaluateOperator.Multiply, step.Amount);
                    break;
                case PhotoEditOperation.Saturation:
                case PhotoEditOperation.Vibrance:
                    image.Modulate(new Percentage(100), new Percentage(step.Amount));
                    break;
                case PhotoEditOperation.Dehaze:
                    image.SigmoidalContrast(step.Amount, 0.5);
                    break;
                case PhotoEditOperation.NoiseReduction:
                    image.MedianFilter((uint)step.Amount);
                    break;
                case PhotoEditOperation.Clarity:
                    image.UnsharpMask(12.0, 8.0, step.Amount, 0.0);
                    break;
                case PhotoEditOperation.Sharpness:
                    image.Sharpen(1.0, step.Amount);
                    break;
                case PhotoEditOperation.Vignette:
                    ApplyVignette(image, step.Amount);
                    break;
                case PhotoEditOperation.Grain:
                    image.AddNoise(NoiseType.Gaussian, step.Amount);
                    break;
            }
        }
    }

    /// <summary>Maps the 256-entry curve through every colour channel in one lookup pass.</summary>
    private static void ApplyToneCurve(MagickImage image, byte[] curve)
    {
        var pixels = new byte[256 * 3];
        for (var i = 0; i < 256; i++)
        {
            var value = curve[i];
            pixels[i * 3] = value;
            pixels[i * 3 + 1] = value;
            pixels[i * 3 + 2] = value;
        }

        using var lookup = new MagickImage();
        lookup.ReadPixels(pixels, new PixelReadSettings(256u, 1u, StorageType.Char, PixelMapping.RGB));
        image.Clut(lookup, Channels.RGB);
    }

    /// <summary>
    /// Darkness at the corners: a black layer whose alpha follows <see cref="VignetteAlpha"/>, composited
    /// over the colour channels only, so a transparent PNG keeps its alpha channel.
    /// </summary>
    private static void ApplyVignette(MagickImage image, double strength)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;
        if (strength <= 0 || width <= 0 || height <= 0)
            return;

        // Build a small mask and scale it up: the falloff is smooth, so this is visually identical and
        // keeps a 4000 px photo from costing 48 MB of per-pixel work.
        var scale = Math.Min(1.0, VignetteMaskMaxSide / (double)Math.Max(width, height));
        var maskWidth = Math.Max(2, (int)Math.Round(width * scale));
        var maskHeight = Math.Max(2, (int)Math.Round(height * scale));
        var pixels = new byte[maskWidth * maskHeight * 4];
        var centreX = (maskWidth - 1) / 2.0;
        var centreY = (maskHeight - 1) / 2.0;
        var corner = Math.Sqrt(centreX * centreX + centreY * centreY);
        for (var y = 0; y < maskHeight; y++)
        {
            for (var x = 0; x < maskWidth; x++)
            {
                var dx = x - centreX;
                var dy = y - centreY;
                var distance = corner <= 0 ? 0 : Math.Sqrt(dx * dx + dy * dy) / corner;
                var alpha = (byte)Math.Round(VignetteAlpha(strength, distance) * 255);
                pixels[(y * maskWidth + x) * 4 + 3] = alpha;
            }
        }

        using var shade = new MagickImage();
        shade.ReadPixels(pixels, new PixelReadSettings((uint)maskWidth, (uint)maskHeight, StorageType.Char, PixelMapping.RGBA));
        var geometry = new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true };
        shade.Resize(geometry);
        image.Composite(shade, CompositeOperator.Over, Channels.RGB);
    }

    /// <summary>
    /// Grey and CMYK images have no RGB channels for the operations above; convert them so an edit is
    /// possible at all. Every other colour space is left exactly as it was read.
    /// </summary>
    private static void NormalizeChannels(MagickImage image)
    {
        if (image.ColorSpace is ColorSpace.Gray or ColorSpace.CMYK)
            image.ColorSpace = ColorSpace.sRGB;
    }

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    /// <summary>Maps the enum onto its spec array once, so lookups cannot drift from the declaration.</summary>
    private static PhotoParameterSpec[] Index()
    {
        var values = Enum.GetValues<PhotoParameter>();
        var indexed = new PhotoParameterSpec[values.Length];
        foreach (var spec in Specs)
            indexed[(int)spec.Parameter] = spec;
        return indexed;
    }
}

/// <summary>
/// The current slider values. Kept mutable so a slider can write into it while dragging; callers hand a
/// <see cref="Clone"/> (or <see cref="Normalized"/>) to the renderer so a background render never sees a
/// half-updated set.
/// </summary>
public sealed class PhotoParameters
{
    private readonly int[] _values = new int[PhotoEdit.Specs.Count];

    public PhotoParameters() => Reset();

    /// <summary>Reads or writes one parameter without a named property.</summary>
    public int this[PhotoParameter parameter]
    {
        get => _values[(int)parameter];
        set => _values[(int)parameter] = value;
    }

    public int Exposure { get => this[PhotoParameter.Exposure]; set => this[PhotoParameter.Exposure] = value; }
    public int Contrast { get => this[PhotoParameter.Contrast]; set => this[PhotoParameter.Contrast] = value; }
    public int Highlights { get => this[PhotoParameter.Highlights]; set => this[PhotoParameter.Highlights] = value; }
    public int Shadows { get => this[PhotoParameter.Shadows]; set => this[PhotoParameter.Shadows] = value; }
    public int Whites { get => this[PhotoParameter.Whites]; set => this[PhotoParameter.Whites] = value; }
    public int Blacks { get => this[PhotoParameter.Blacks]; set => this[PhotoParameter.Blacks] = value; }
    public int Saturation { get => this[PhotoParameter.Saturation]; set => this[PhotoParameter.Saturation] = value; }
    public int Vibrance { get => this[PhotoParameter.Vibrance]; set => this[PhotoParameter.Vibrance] = value; }
    public int Temperature { get => this[PhotoParameter.Temperature]; set => this[PhotoParameter.Temperature] = value; }
    public int Tint { get => this[PhotoParameter.Tint]; set => this[PhotoParameter.Tint] = value; }
    public int Sharpness { get => this[PhotoParameter.Sharpness]; set => this[PhotoParameter.Sharpness] = value; }
    public int Clarity { get => this[PhotoParameter.Clarity]; set => this[PhotoParameter.Clarity] = value; }
    public int Dehaze { get => this[PhotoParameter.Dehaze]; set => this[PhotoParameter.Dehaze] = value; }
    public int Grain { get => this[PhotoParameter.Grain]; set => this[PhotoParameter.Grain] = value; }
    public int Vignette { get => this[PhotoParameter.Vignette]; set => this[PhotoParameter.Vignette] = value; }

    public int NoiseReduction
    {
        get => this[PhotoParameter.NoiseReduction];
        set => this[PhotoParameter.NoiseReduction] = value;
    }

    /// <summary>Every slider back to its default (all zero today).</summary>
    public void Reset()
    {
        foreach (var spec in PhotoEdit.Specs)
            this[spec.Parameter] = spec.Default;
    }

    /// <summary>Clamps every value into its declared range, in place.</summary>
    public void Clamp()
    {
        foreach (var spec in PhotoEdit.Specs)
            this[spec.Parameter] = spec.Clamp(this[spec.Parameter]);
    }

    /// <summary>A clamped copy, safe to hand to a background render.</summary>
    public PhotoParameters Normalized()
    {
        var copy = Clone();
        copy.Clamp();
        return copy;
    }

    public PhotoParameters Clone()
    {
        var copy = new PhotoParameters();
        Array.Copy(_values, copy._values, _values.Length);
        return copy;
    }
}
