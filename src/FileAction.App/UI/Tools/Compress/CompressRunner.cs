using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Compress;

/// <summary>
/// Writes the compressed copy: Magick.NET for images, PDFsharp for PDFs and FFmpeg for audio/video
/// (two passes when a bitrate target is used). Every path writes a hidden temporary file and then moves
/// it to a unique name, so an existing file is never overwritten and a failure leaves nothing behind.
/// </summary>
internal static class CompressRunner
{
    private static readonly Regex TimeRegex = new(@"\btime=\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>Images: optional resize, then quality / palette decisions for the container.</summary>
    public static async Task<string> ImageAsync(
        FileItem item,
        CompressionPlan plan,
        string directory,
        CancellationToken ct,
        IProgress<double> progress)
    {
        var format = ImageBackend.OutputFormatFor(Formats.Normalize(item.Format));
        return await OutputPaths.ProduceAsync(directory, OutputName(item), format, tmp => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var image = new MagickImage(item.Path);
            image.AutoOrient();

            if (plan.Resized && plan.Width > 0 && plan.Height > 0)
                image.Resize(new MagickGeometry((uint)plan.Width, (uint)plan.Height));

            ApplyQuality(image, format, plan.Quality);
            ct.ThrowIfCancellationRequested();
            image.Write(tmp, ImageBackend.ToMagickFormat(format));
            progress.Report(1);
        }, ct)).ConfigureAwait(false);
    }

    /// <summary>PDF: re-save with compressed content streams; the text stays selectable.</summary>
    public static Task<string> PdfAsync(FileItem item, string directory, CancellationToken ct) =>
        OutputPaths.ProduceAsync(directory, OutputName(item), "pdf", tmp => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            PdfBackend.Optimize(item.Path, tmp);
        }, ct));

    /// <summary>
    /// Audio/video: one FFmpeg run, or two when <see cref="CompressionPlan.TwoPass"/> is set. Pass 1 writes
    /// no file; pass 2 reuses its statistics to approach the bitrate the target size needs.
    /// </summary>
    public static async Task<string> MediaAsync(
        FileItem item,
        CompressionPlan plan,
        double durationSeconds,
        string directory,
        ToolLocator tools,
        CancellationToken ct,
        IProgress<double> progress)
    {
        var ffmpeg = tools.Require(ExternalTool.Ffmpeg);
        var extension = plan.Kind == MediaKind.Video ? FfmpegBackend.SafeContainer(item.Format) : Formats.Normalize(item.Format);
        var passDirectory = OutputPaths.TempDirectory();
        var passLog = Path.Combine(passDirectory, "ffpass");
        try
        {
            return await OutputPaths.ProduceAsync(directory, OutputName(item), extension, async tmp =>
            {
                if (plan.Kind == MediaKind.Audio)
                {
                    var audio = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", item.Path };
                    audio.AddRange(AudioArgs(plan.LosslessAudio, extension, plan.AudioKbps));
                    audio.Add(tmp);
                    await RunAsync(ffmpeg, audio, durationSeconds, 0, 1, ct, progress).ConfigureAwait(false);
                    return;
                }

                if (plan.TwoPass)
                {
                    var first = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", item.Path };
                    first.AddRange(VideoArgs(extension, plan, pass: 1, passLog));
                    first.AddRange(["-an", "-f", "null", "-"]);
                    await RunAsync(ffmpeg, first, durationSeconds, 0, 0.45, ct, progress).ConfigureAwait(false);

                    var second = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", item.Path };
                    second.AddRange(VideoArgs(extension, plan, pass: 2, passLog));
                    second.AddRange(AudioForVideo(extension, plan.AudioKbps));
                    second.Add(tmp);
                    await RunAsync(ffmpeg, second, durationSeconds, 0.45, 1, ct, progress).ConfigureAwait(false);
                    return;
                }

                var single = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", item.Path };
                single.AddRange(VideoArgs(extension, plan, pass: 0, passLog: null));
                single.AddRange(AudioForVideo(extension, plan.AudioKbps));
                single.Add(tmp);
                await RunAsync(ffmpeg, single, durationSeconds, 0, 1, ct, progress).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(passDirectory);
        }
    }

    /// <summary>"photo Compressed" — the base name every path here writes.</summary>
    private static string OutputName(FileItem item) => $"{item.BaseName} {T("tool.compress.suffix")}";

    private static void ApplyQuality(MagickImage image, string format, int quality)
    {
        image.Quality = (uint)Math.Clamp(quality, 1, 100);
        switch (format)
        {
            case "jpg":
                image.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                image.Settings.Interlace = Interlace.Jpeg;
                break;
            case "png":
                // Lossy palette reduction is the only real size win for PNG.
                if (quality <= 75 && image.TotalColors > 256)
                    image.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                break;
            case "ico":
                if (image.Width > 256 || image.Height > 256)
                    image.Resize(new MagickGeometry(256, 256));
                image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
                break;
        }

        if (format is "jpg" or "bmp" && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
    }

    private static IReadOnlyList<string> VideoArgs(string container, CompressionPlan plan, int pass, string? passLog)
    {
        var args = new List<string>();
        if (container == "webm")
        {
            args.AddRange(["-c:v", "libvpx-vp9", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"]);
            if (plan.TwoPass)
                AddBitrateTarget(args, plan.VideoKbps);
            else
                args.AddRange(["-crf", plan.Crf.ToString(CultureInfo.InvariantCulture), "-b:v", "0"]);
        }
        else
        {
            args.AddRange(["-c:v", "libx264", "-preset", "medium", "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2"]);
            if (plan.TwoPass)
                AddBitrateTarget(args, plan.VideoKbps);
            else
                args.AddRange(["-crf", plan.Crf.ToString(CultureInfo.InvariantCulture)]);
        }

        if (plan.TwoPass && passLog is not null)
            args.AddRange(["-pass", pass.ToString(CultureInfo.InvariantCulture), "-passlogfile", passLog]);

        // The mov muxer options only exist while a real MP4/MOV is written; pass 1 writes to the null muxer
        // and would reject them.
        if (pass != 1 && container is "mp4" or "mov")
            args.AddRange(["-movflags", "+faststart"]);

        return args;
    }

    /// <summary>
    /// The two-pass bitrate target. The matching maxrate/bufsize cap the peaks and keep the average at or
    /// below the requested size; without them x264 spends up to 15 % more than the target on hard footage.
    /// </summary>
    private static void AddBitrateTarget(List<string> args, int videoKbps) =>
        args.AddRange(["-b:v", $"{videoKbps}k", "-maxrate", $"{videoKbps}k", "-bufsize", $"{videoKbps * 2}k"]);

    private static IReadOnlyList<string> AudioForVideo(string container, int kbps) =>
        container == "webm"
            ? ["-c:a", "libopus", "-b:a", $"{kbps}k"]
            : ["-c:a", "aac", "-b:a", $"{kbps}k"];

    private static IReadOnlyList<string> AudioArgs(bool lossless, string format, int kbps) => lossless
        ? format switch
        {
            "flac" => ["-c:a", "flac", "-compression_level", "8"],
            "wav" => ["-c:a", "pcm_s16le"],
            "aiff" => ["-c:a", "pcm_s16be"],
            _ => ["-c:a", "copy"],
        }
        : ["-c:a", AudioCodec(format), "-b:a", $"{kbps}k"];

    private static string AudioCodec(string format) => format switch
    {
        "mp3" => "libmp3lame",
        "m4a" or "aac" => "aac",
        "ogg" => "libvorbis",
        "opus" => "libopus",
        "wma" => "wmav2",
        _ => "aac",
    };

    /// <summary>Runs FFmpeg and maps its <c>time=</c> banner onto the progress range of this pass.</summary>
    private static async Task RunAsync(
        string ffmpeg,
        IReadOnlyList<string> args,
        double durationSeconds,
        double from,
        double to,
        CancellationToken ct,
        IProgress<double> progress)
    {
        await ProcessRunner.RunAsync(ffmpeg, args, ct, line =>
        {
            if (durationSeconds <= 0)
                return;
            var match = TimeRegex.Match(line);
            if (!match.Success)
                return;
            var seconds = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var fraction = Math.Clamp(seconds / durationSeconds, 0, 1);
            progress.Report(from + (to - from) * fraction);
        }).ConfigureAwait(false);
    }
}
