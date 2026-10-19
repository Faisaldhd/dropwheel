using System.Globalization;

namespace FileAction.Core;

/// <summary>The kind of file a compression request applies to.</summary>
public enum MediaKind
{
    /// <summary>The file is not something the compress tool handles.</summary>
    None,

    Image,
    Audio,
    Video,
    Pdf,
}

/// <summary>How the user asked for the result: an absolute target, a percentage or a quality preset.</summary>
public enum CompressionMode
{
    /// <summary>"Target size (MB)": the encoder aims for an absolute file size.</summary>
    TargetSize,

    /// <summary>"Percentage": keep this share of the source size.</summary>
    Percentage,

    /// <summary>"Preset": pick quality by High / Medium / Low.</summary>
    Preset,
}

/// <summary>The quality presets shared by images, audio and video.</summary>
public enum CompressionPreset
{
    High,
    Medium,
    Low,
}

/// <summary>
/// Everything the compress window collects, as one immutable value. <see cref="Format"/> is the
/// normalized extension ("jpg", "mp4"), because the plan depends on the codec family.
/// </summary>
public readonly record struct CompressionRequest(
    MediaKind Kind,
    string Format,
    CompressionMode Mode,
    double TargetMegabytes,
    int Percentage,
    CompressionPreset Preset,
    int MaxLongSide)
{
    /// <summary>Preset mode with the medium quality; the source size is only known after the probe.</summary>
    public static CompressionRequest Default(MediaKind kind, string format) =>
        new(kind, format, CompressionMode.Preset, 1.0, 60, CompressionPreset.Medium, 0);
}

/// <summary>The resize and quality decision for one image, in source pixels.</summary>
public readonly record struct ImagePlan(int Width, int Height, int Quality, int SourceWidth, int SourceHeight)
{
    /// <summary>True when the output is smaller than the source in either dimension.</summary>
    public bool Resized => Width > 0 && Height > 0 && (Width != SourceWidth || Height != SourceHeight);
}

/// <summary>
/// What the encoders are told and what the window shows as the estimate. Pure data, so the whole
/// decision is unit tested without a window, a file or an external tool.
/// </summary>
public readonly record struct CompressionPlan(
    CompressionRequest Request,
    int SourceWidth,
    int SourceHeight,
    int Width,
    int Height,
    int Quality,
    int Crf,
    int AudioKbps,
    int VideoKbps,
    bool TwoPass,
    bool LosslessAudio,
    long EstimatedBytes)
{
    public MediaKind Kind => Request.Kind;

    /// <summary>True when the image plan changes the pixel size.</summary>
    public bool Resized => Width > 0 && Height > 0 && (Width != SourceWidth || Height != SourceHeight);
}

/// <summary>
/// The compression rules: which files the tool handles, the quality / CRF / bitrate tables, the image
/// resize plan (including the automatic downscale that target-size mode needs) and the size estimate.
/// Nothing here touches the disk, a window or an external program.
/// </summary>
public static class Compression
{
    /// <summary>Smallest target size the window accepts, in megabytes.</summary>
    public const double MinTargetMegabytes = 0.02;

    /// <summary>Largest target size the window accepts, in megabytes.</summary>
    public const double MaxTargetMegabytes = 20480;

    /// <summary>Smallest percentage the window accepts.</summary>
    public const int MinPercentage = 5;

    /// <summary>Largest percentage the window accepts; 100 means "re-encode at the same size".</summary>
    public const int MaxPercentage = 100;

    /// <summary>Smallest long side, in pixels, that the automatic downscale may reach.</summary>
    public const int MinLongSide = 64;

    /// <summary>Largest long side, in pixels, that the resize box accepts.</summary>
    public const int MaxLongSide = 20000;

    /// <summary>Lowest video bitrate the two-pass encode is allowed to ask for.</summary>
    public const int MinVideoKbps = 80;

    /// <summary>Highest video bitrate the two-pass encode is allowed to ask for.</summary>
    public const int MaxVideoKbps = 120_000;

    /// <summary>Lowest audio bitrate the encode is allowed to ask for.</summary>
    public const int MinAudioKbps = 32;

    /// <summary>Highest audio bitrate the encode is allowed to ask for.</summary>
    public const int MaxAudioKbps = 512;

    /// <summary>Normalized media kind of a format key ("jpeg" is normalized first by <see cref="Formats"/>).</summary>
    public static MediaKind KindOf(string? format)
    {
        var key = Formats.Normalize(format ?? string.Empty);
        if (key.Length == 0)
            return MediaKind.None;
        if (Formats.Pdf.Contains(key))
            return MediaKind.Pdf;
        if (Formats.Images.Contains(key))
            return MediaKind.Image;
        if (Formats.Audio.Contains(key))
            return MediaKind.Audio;
        if (Formats.Video.Contains(key))
            return MediaKind.Video;
        return MediaKind.None;
    }

    /// <summary>WAV, AIFF and FLAC are lossless: a bitrate target cannot shrink them.</summary>
    public static bool IsLosslessAudio(string? format) =>
        Formats.Normalize(format ?? string.Empty) is "wav" or "aiff" or "flac";

    /// <summary>True when the mode needs a duration before a bitrate can be computed.</summary>
    public static bool NeedsDuration(MediaKind kind, CompressionMode mode) =>
        mode != CompressionMode.Preset && kind is MediaKind.Audio or MediaKind.Video;

    // ------------------------------------------------------------------ quality tables

    /// <summary>Encoder quality for a preset (JPEG / WebP / AVIF, 1–100).</summary>
    public static int QualityFor(CompressionPreset preset) => preset switch
    {
        CompressionPreset.High => 86,
        CompressionPreset.Medium => 72,
        CompressionPreset.Low => 55,
        _ => 72,
    };

    /// <summary>x264 CRF for a preset: lower is better quality and bigger.</summary>
    public static int CrfFor(CompressionPreset preset) => preset switch
    {
        CompressionPreset.High => 21,
        CompressionPreset.Medium => 25,
        CompressionPreset.Low => 30,
        _ => 25,
    };

    /// <summary>Audio bitrate for a preset, in kbps.</summary>
    public static int AudioKbpsFor(CompressionPreset preset) => preset switch
    {
        CompressionPreset.High => 192,
        CompressionPreset.Medium => 128,
        CompressionPreset.Low => 96,
        _ => 128,
    };

    /// <summary>Rough JPEG bytes per pixel for a quality; only ever used for estimates.</summary>
    public static double BytesPerPixel(int quality)
    {
        var q = Math.Clamp(quality, 1, 100);
        return 2.5e-6 * Math.Pow(q, 2.64);
    }

    /// <summary>How many bytes a quality keeps, relative to a source encoded at quality 90.</summary>
    public static double QualityRatio(int quality)
    {
        var reference = BytesPerPixel(90);
        if (reference <= 0)
            return 1.0;
        return Math.Clamp(BytesPerPixel(quality) / reference, 0.03, 2.5);
    }

    /// <summary>
    /// Container factor for the estimate. PNG/GIF are palette formats (their size barely follows the
    /// "quality"), BMP/TIFF stay mostly uncompressed, so they need their own multiplier.
    /// </summary>
    public static double FormatFactor(string? format) =>
        Formats.Normalize(format ?? string.Empty) switch
        {
            "png" => 1.6,
            "gif" => 0.9,
            "bmp" => 1.0,
            "tiff" => 1.4,
            "ico" => 0.6,
            _ => 1.0,
        };

    /// <summary>
    /// How much of the source an image re-encode keeps for a quality, including the palette formats.
    /// This is the number the estimate is built from; it is deliberately conservative.
    /// </summary>
    public static double ImageBytesRatio(string? format, int quality)
    {
        var key = Formats.Normalize(format ?? string.Empty);
        var q = Math.Clamp(quality, 1, 100);
        return key switch
        {
            // Magick quantizes PNG/GIF to a palette for the lower qualities; the win is large and fixed.
            "png" => q <= 60 ? 0.30 : q <= 75 ? 0.50 : 0.90,
            "gif" => q <= 60 ? 0.55 : q <= 75 ? 0.75 : 0.95,
            "bmp" => 1.0,
            "tiff" => q <= 60 ? 0.80 : 0.95,
            "ico" => 0.60,
            _ => QualityRatio(q),
        };
    }

    // ------------------------------------------------------------------ clamping

    public static double ClampMegabytes(double megabytes) =>
        double.IsFinite(megabytes) ? Math.Clamp(megabytes, MinTargetMegabytes, MaxTargetMegabytes) : MinTargetMegabytes;

    public static int ClampPercentage(int percentage) => Math.Clamp(percentage, MinPercentage, MaxPercentage);

    public static int ClampLongSide(int longSide) => Math.Clamp(longSide, MinLongSide, MaxLongSide);

    /// <summary>Megabytes as bytes (1 MB = 1024 × 1024, the number the box shows).</summary>
    public static long BytesFromMegabytes(double megabytes) =>
        (long)Math.Round(ClampMegabytes(megabytes) * 1024 * 1024);

    public static double MegabytesFromBytes(long bytes) => bytes <= 0 ? 0 : bytes / (1024.0 * 1024.0);

    // ------------------------------------------------------------------ bitrate math

    /// <summary>Total bitrate (kbps) that fits <paramref name="bytes"/> into <paramref name="durationSeconds"/>.</summary>
    public static int BitrateKbps(long bytes, double durationSeconds)
    {
        if (bytes <= 0 || durationSeconds <= 0 || !double.IsFinite(durationSeconds))
            return 0;
        return Math.Max(1, (int)Math.Round(bytes * 8.0 / durationSeconds / 1000.0));
    }

    /// <summary>Video bitrate from the total: audio is paid for first, then clamped to what x264 accepts.</summary>
    public static int VideoKbps(int totalKbps, int audioKbps) =>
        Math.Clamp(totalKbps - Math.Max(0, audioKbps), MinVideoKbps, MaxVideoKbps);

    /// <summary>Audio bitrate from the total, clamped to what the codecs accept.</summary>
    public static int AudioKbps(int totalKbps) => Math.Clamp(totalKbps, MinAudioKbps, MaxAudioKbps);

    /// <summary>How many bytes a duration at a total bitrate produces; the encode's honest lower bound.</summary>
    public static long EstimateFromBitrate(double durationSeconds, int totalKbps)
    {
        if (durationSeconds <= 0 || totalKbps <= 0 || !double.IsFinite(durationSeconds))
            return 0;
        return (long)Math.Round(durationSeconds * totalKbps * 1000.0 / 8.0);
    }

    // ------------------------------------------------------------------ estimates

    /// <summary>A per-kind factor for preset mode, where no bitrate is computed.</summary>
    public static double PresetFactor(MediaKind kind, CompressionPreset preset) => kind switch
    {
        MediaKind.Audio => preset switch
        {
            CompressionPreset.High => 0.80,
            CompressionPreset.Medium => 0.60,
            _ => 0.42,
        },
        MediaKind.Video => preset switch
        {
            CompressionPreset.High => 0.65,
            CompressionPreset.Medium => 0.45,
            _ => 0.30,
        },
        MediaKind.Pdf => preset switch
        {
            CompressionPreset.High => 0.90,
            CompressionPreset.Medium => 0.78,
            _ => 0.62,
        },
        MediaKind.Image => preset switch
        {
            CompressionPreset.High => 0.80,
            CompressionPreset.Medium => 0.55,
            _ => 0.35,
        },
        _ => 1.0,
    };

    /// <summary>The estimated size of a re-encoded image, from the planned pixels and quality.</summary>
    public static long EstimateImageBytes(long sourceBytes, string? format, ImagePlan plan)
    {
        if (sourceBytes <= 0)
            return 0;

        var ratio = ImageBytesRatio(format, plan.Quality);
        if (plan.SourceWidth > 0 && plan.SourceHeight > 0 && plan.Width > 0 && plan.Height > 0)
        {
            var sourcePixels = (double)plan.SourceWidth * plan.SourceHeight;
            if (sourcePixels > 0)
                ratio *= (double)plan.Width * plan.Height / sourcePixels;
        }

        var estimated = (long)Math.Round(sourceBytes * Math.Clamp(ratio, 0.01, 2.0));
        return Math.Max(1, Math.Min(estimated, sourceBytes));
    }

    /// <summary>How much smaller the estimate is than the source, in whole percent (0 when it is not).</summary>
    public static int PercentSmaller(long sourceBytes, long estimatedBytes)
    {
        if (sourceBytes <= 0)
            return 0;
        var kept = Math.Clamp(estimatedBytes, 0, sourceBytes);
        return (int)Math.Round((1 - (double)kept / sourceBytes) * 100);
    }

    // ------------------------------------------------------------------ planning

    /// <summary>
    /// The image plan: quality from the mode, the user's max long side, and — for target size and
    /// percentage — an automatic downscale so the estimate can actually reach the goal. Percentage mode
    /// keeps the best quality and scales the pixels instead of dropping the encoder quality. Never upscales.
    /// </summary>
    public static ImagePlan PlanImage(
        int width,
        int height,
        long sourceBytes,
        string? format,
        CompressionMode mode,
        double targetMegabytes,
        int percentage,
        CompressionPreset preset,
        int maxLongSide)
    {
        var quality = mode switch
        {
            CompressionMode.Preset => QualityFor(preset),
            CompressionMode.TargetSize => QualityFor(CompressionPreset.Medium),
            _ => QualityFor(CompressionPreset.High),
        };

        if (width <= 0 || height <= 0)
            return new ImagePlan(0, 0, quality, Math.Max(0, width), Math.Max(0, height));

        var longSide = Math.Max(width, height);
        var scale = 1.0;
        if (maxLongSide > 0 && longSide > maxLongSide)
            scale = (double)ClampLongSide(maxLongSide) / longSide;

        var goal = mode switch
        {
            CompressionMode.TargetSize => (double)BytesFromMegabytes(targetMegabytes),
            CompressionMode.Percentage => sourceBytes * ClampPercentage(percentage) / 100.0,
            _ => 0,
        };

        if (goal > 0 && sourceBytes > 0)
        {
            var atFullSize = sourceBytes * Math.Max(0.01, ImageBytesRatio(format, quality));
            if (atFullSize > goal)
            {
                var pixelScale = Math.Sqrt(goal / atFullSize);
                if (pixelScale < scale)
                    scale = pixelScale;
            }
        }

        var (plannedWidth, plannedHeight) = Scale(width, height, scale);

        // Keep the result readable: never below 64 px on the long side (unless the source is smaller).
        var floorSide = Math.Min(MinLongSide, longSide);
        if (scale < 1 && Math.Max(plannedWidth, plannedHeight) < floorSide)
            (plannedWidth, plannedHeight) = Scale(width, height, (double)floorSide / longSide);

        return new ImagePlan(plannedWidth, plannedHeight, quality, width, height);
    }

    /// <summary>The full plan for a compression request; the single entry point the window uses.</summary>
    public static CompressionPlan Plan(
        CompressionRequest request,
        long sourceBytes,
        int sourceWidth,
        int sourceHeight,
        double durationSeconds)
    {
        var kind = request.Kind;
        var quality = QualityFor(request.Preset);
        var crf = CrfFor(request.Preset);
        var audioKbps = AudioKbpsFor(request.Preset);
        var videoKbps = 0;
        var twoPass = false;
        var lossless = false;
        var width = Math.Max(0, sourceWidth);
        var height = Math.Max(0, sourceHeight);
        var estimated = Math.Max(0, sourceBytes);

        switch (kind)
        {
            case MediaKind.Image:
            {
                var image = PlanImage(sourceWidth, sourceHeight, sourceBytes, request.Format, request.Mode,
                    request.TargetMegabytes, request.Percentage, request.Preset, request.MaxLongSide);
                quality = image.Quality;
                width = image.Width;
                height = image.Height;
                estimated = EstimateImageBytes(sourceBytes, request.Format, image);
                break;
            }

            case MediaKind.Audio:
            {
                lossless = IsLosslessAudio(request.Format);
                if (lossless)
                {
                    audioKbps = AudioKbpsFor(request.Preset);
                    estimated = EstimateFromFactor(sourceBytes, 0.97);
                }
                else if (request.Mode == CompressionMode.Preset || durationSeconds <= 0)
                {
                    audioKbps = AudioKbpsFor(request.Preset);
                    estimated = EstimateFromFactor(sourceBytes, PresetFactor(kind, request.Preset));
                }
                else
                {
                    audioKbps = AudioKbps(TotalKbps(request, sourceBytes, durationSeconds));
                    estimated = Math.Min(sourceBytes, Math.Max(GoalBytes(request, sourceBytes),
                        EstimateFromBitrate(durationSeconds, audioKbps)));
                }
                break;
            }

            case MediaKind.Video:
            {
                if (request.Mode == CompressionMode.Preset || durationSeconds <= 0)
                {
                    estimated = EstimateFromFactor(sourceBytes, PresetFactor(kind, request.Preset));
                }
                else
                {
                    twoPass = true;
                    videoKbps = VideoKbps(TotalKbps(request, sourceBytes, durationSeconds), audioKbps);
                    estimated = Math.Min(sourceBytes, Math.Max(GoalBytes(request, sourceBytes),
                        EstimateFromBitrate(durationSeconds, videoKbps + audioKbps)));
                }
                break;
            }

            case MediaKind.Pdf:
            {
                estimated = request.Mode switch
                {
                    CompressionMode.TargetSize => Math.Min(sourceBytes, BytesFromMegabytes(request.TargetMegabytes)),
                    CompressionMode.Percentage => EstimateFromPercentage(sourceBytes, request.Percentage),
                    _ => EstimateFromFactor(sourceBytes, PresetFactor(kind, request.Preset)),
                };
                break;
            }
        }

        return new CompressionPlan(request, sourceWidth, sourceHeight, width, height,
            quality, crf, audioKbps, videoKbps, twoPass, lossless, estimated);
    }

    // ------------------------------------------------------------------ formatting

    /// <summary>"824 B", "12.4 KB", "1.8 MB", "2.25 GB" — Western digits in every language.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        if (bytes < 1024)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        if (bytes < 1024L * 1024)
            return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024)
            return (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>"0:42", "12:05", "1:02:03" — Western digits, no localization.</summary>
    public static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var span = TimeSpan.FromSeconds(Math.Round(seconds));
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
    }

    /// <summary>"512×384" — Western digits.</summary>
    public static string FormatPixels(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width}×{height}");

    /// <summary>Formats a megabyte value for a text box: "1.5", "20", "0.8".</summary>
    public static string FormatMegabytes(double megabytes) =>
        ClampMegabytes(megabytes).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Parses a megabyte box value; false when the text is not a number in range.</summary>
    public static bool TryParseMegabytes(string? text, out double megabytes)
    {
        megabytes = 0;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
            return false;
        // Western digits and either decimal separator, whatever the UI language is.
        if (!double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && !double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            return false;
        if (!double.IsFinite(value) || value < MinTargetMegabytes || value > MaxTargetMegabytes)
            return false;
        megabytes = value;
        return true;
    }

    /// <summary>Parses a percentage box value; false when the text is not a whole number in range.</summary>
    public static bool TryParsePercentage(string? text, out int percentage)
    {
        percentage = 0;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
            return false;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            return false;
        if (value < MinPercentage || value > MaxPercentage)
            return false;
        percentage = value;
        return true;
    }

    /// <summary>Parses a maximum-long-side box value; empty means "keep the source size".</summary>
    public static bool TryParseLongSide(string? text, out int longSide)
    {
        longSide = 0;
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
            return true;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && !int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            return false;
        if (value < MinLongSide || value > MaxLongSide)
            return false;
        longSide = value;
        return true;
    }

    // ------------------------------------------------------------------ internals

    private static int TotalKbps(CompressionRequest request, long sourceBytes, double durationSeconds)
    {
        if (request.Mode == CompressionMode.TargetSize)
            return BitrateKbps(BytesFromMegabytes(request.TargetMegabytes), durationSeconds);
        var sourceKbps = BitrateKbps(sourceBytes, durationSeconds);
        return Math.Max(1, (int)Math.Round(sourceKbps * ClampPercentage(request.Percentage) / 100.0));
    }

    /// <summary>The size the user asked for, in bytes (the target or the percentage of the source).</summary>
    private static long GoalBytes(CompressionRequest request, long sourceBytes) => request.Mode switch
    {
        CompressionMode.TargetSize => BytesFromMegabytes(request.TargetMegabytes),
        CompressionMode.Percentage => EstimateFromPercentage(sourceBytes, request.Percentage),
        _ => 0,
    };

    private static long EstimateFromPercentage(long sourceBytes, int percentage) =>
        sourceBytes <= 0
            ? 0
            : Math.Max(1, Math.Min(sourceBytes, (long)Math.Round(sourceBytes * ClampPercentage(percentage) / 100.0)));

    private static long EstimateFromFactor(long sourceBytes, double factor)
    {
        if (sourceBytes <= 0)
            return 0;
        var estimated = (long)Math.Round(sourceBytes * Math.Clamp(factor, 0.02, 1.0));
        return Math.Clamp(estimated, 1, sourceBytes);
    }

    private static (int Width, int Height) Scale(int width, int height, double scale)
    {
        var value = Math.Clamp(scale, 0, 1);
        return (Math.Max(1, (int)Math.Round(width * value)), Math.Max(1, (int)Math.Round(height * value)));
    }
}
