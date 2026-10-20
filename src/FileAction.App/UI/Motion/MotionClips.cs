using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Actions;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;
using ImageMagick;

namespace FileAction.App;

/// <summary>
/// Renders the website's short motion clips from the real visual tree: the wheel opening with its
/// travelling highlight, and every registered tool window opening and closing, in dark and light.
/// Frames are posed with the same maths and easing curves the live animations use, then encoded as
/// small animated WebP files with a PNG poster frame. Nothing is screen-recorded: CI has no
/// interactive desktop, so every frame is a deterministic render of the shipped UI.
/// </summary>
internal static class MotionClips
{
    private const uint ClipWidth = 480;
    private const int FrameDelay = 8; // 1/100 s → 12.5 fps
    private static readonly double[] WheelTimes = [0, 80, 160, 240, 320, 400, 480, 560, 640, 720, 800, 880, 960, 1040, 1120, 1200, 1260, 1300];

    internal static void Run(string outDir, List<string> log)
    {
        var directory = Path.Combine(outDir, "motion");
        Directory.CreateDirectory(directory);
        var framesDir = Path.Combine(Path.GetTempPath(), "DropWheel-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(framesDir);
        var clips = new List<string>();
        try
        {
            var settings = new AppSettings();
            var tools = new ToolLocator();
            var core = WheelController.BuildOperations();
            var actions = FileActions.Build(settings, () => { });
            IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
                Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);
            var photo = new List<FileItem> { new(@"C:\Photos\vacation.jpg") };

            var sample = Path.Combine(directory, "motion-sample.png");
            if (!File.Exists(sample))
            {
                using var image = new MagickImage(MagickColors.RebeccaPurple, 640, 420);
                image.Write(sample);
            }

            foreach (var dark in new[] { true, false })
            {
                var theme = dark ? "dark" : "light";
                Theme.Override = dark;
                Theme.Refresh();
                try
                {
                    var name = $"wheel-{theme}";
                    var wheel = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Source, (_, _, _) => { });
                    var root = wheel.DetachForCapture();
                    var poster = Path.Combine(directory, name + "-poster.png");
                    var rendered = RenderFrames(root, WheelTimes, t =>
                    {
                        var (to, from, travel) = MotionSelfTest.HighlightAt(t);
                        var close = Math.Max(0, t - 1200);
                        wheel.Pose(t, to, from, travel, close);
                    }, framesDir, name, posterAt: 400, poster);
                    clips.Add(Encode(rendered, Path.Combine(directory, name + ".webp")));
                    wheel.Close();
                    log.Add($"ok motion/{name}.webp ({rendered.Count} frames)");
                }
                finally
                {
                    Theme.Override = null;
                    Theme.Refresh();
                }
            }

            foreach (var spec in ToolRegistry.All)
            {
                var item = new FileItem(sample);
                if (!spec.Applies([item]))
                {
                    log.Add($"skipped motion/{spec.Id} (no sample)");
                    continue;
                }
                foreach (var dark in new[] { true, false })
                {
                    Theme.Override = dark;
                    Theme.Refresh();
                    try
                    {
                        var theme = dark ? "dark" : "light";
                        var name = "tool-" + spec.Id.Replace('.', '-') + "-" + theme;
                        var window = new ToolWindow(spec, [item], null);
                        try
                        {
                            SelfTest.Pump(() => window.PrepareAsync(animate: false));
                            var root = window.DetachForCapture();
                            var poster = Path.Combine(directory, name + "-poster.png");
                            var times = new double[] { 0, 40, 80, 120, 160, 180, 300, 500, 760, 820 };
                            var rendered = RenderFrames(root, times, t =>
                            {
                                window.Pose(Math.Min(t, Motion.WindowOpenMs), Math.Max(0, t - 700));
                            }, framesDir, name, posterAt: 500, poster);
                            clips.Add(Encode(rendered, Path.Combine(directory, name + ".webp")));
                            log.Add($"ok motion/{name}.webp ({rendered.Count} frames)");
                        }
                        finally
                        {
                            window.Close();
                        }
                    }
                    finally
                    {
                        Theme.Override = null;
                        Theme.Refresh();
                    }
                }
            }

            var bytes = clips.Sum(c => new FileInfo(c).Length) / 1024.0;
            log.Add($"ok motion/clips ({clips.Count} clips, {bytes:F0} KB, {ClipWidth} px wide, {FrameDelay * 10} ms/frame)");
        }
        finally
        {
            Theme.Override = null;
            Theme.Refresh();
            try
            {
                Directory.Delete(framesDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp frames are best-effort cleanup; the clips are already encoded.
            }
        }
    }

    /// <summary>Renders one timeline into numbered PNG frames and the posters; returns the frame paths.</summary>
    private static List<string> RenderFrames(FrameworkElement root, IReadOnlyList<double> times, Action<double> pose,
        string directory, string name, double posterAt, string posterPath)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.Width));
        var height = Math.Max(1, (int)Math.Ceiling(root.Height));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var frames = new List<string>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            pose(times[i]);
            root.UpdateLayout();
            bitmap.Clear();
            bitmap.Render(root);
            var file = Path.Combine(directory, $"{name}-{i:000}.png");
            using var stream = File.Create(file);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
            frames.Add(file);
        }

        // Poster: the fully-open frame, scaled to the same web width.
        var posterIndex = Math.Clamp(times.ToList().FindLastIndex(t => t <= posterAt), 0, frames.Count - 1);
        using var posterImage = new MagickImage(frames[posterIndex]);
        Resize(posterImage);
        posterImage.Quality = 88;
        posterImage.Write(posterPath);
        return frames;
    }

    /// <summary>Encodes the PNG frames as one looping animated WebP and returns its path.</summary>
    private static string Encode(IReadOnlyList<string> frames, string path)
    {
        using var collection = new MagickImageCollection();
        foreach (var frame in frames)
        {
            var image = new MagickImage(frame);
            Resize(image);
            image.AnimationDelay = FrameDelay;
            image.AnimationIterations = 0;
            image.Quality = 78;
            collection.Add(image);
        }
        using var stream = File.Create(path);
        collection.Write(stream, MagickFormat.WebP);
        return path;
    }

    private static void Resize(MagickImage image)
    {
        if (image.Width <= ClipWidth)
            return;
        var height = (uint)Math.Max(1, Math.Round(image.Height * (double)ClipWidth / image.Width));
        image.Resize(ClipWidth, height);
    }
}
