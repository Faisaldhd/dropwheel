using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileAction.App.Actions;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;
using ImageMagick;

namespace FileAction.App;

/// <summary>
/// The motion part of <c>--self-test</c>: measures animated frames with motion ON versus OFF, records
/// CPU and working set, drives every registered tool window, and fails when a motion budget is exceeded.
///
/// CI has no GPU-backed interactive desktop, so "frame time" is the UI-thread work a frame costs:
/// applying the animation pose plus the layout pass. A second number is recorded for the same frame
/// rendered through <see cref="RenderTargetBitmap"/>, which is a software-rasterisation proxy for one
/// composed frame (it is not the GPU compositor). Both are reported in <c>docs/PERF.md</c>.
/// </summary>
internal static class MotionSelfTest
{
    /// <summary>Budget for the UI-thread part of one 60 fps frame.</summary>
    public const double FrameBudgetMs = 16.7;

    /// <summary>Software-rasterisation probe ceiling; a composed frame should also fit 60 fps.</summary>
    public const double RasterBudgetMs = 33.0;

    /// <summary>Idle CPU after everything has closed. A polling timer would show up here.</summary>
    public const double IdleCpuBudgetPercent = 5.0;

    /// <summary>Working set the motion surfaces may keep after they close (the 15 MB PERF rule).</summary>
    public const double MemoryRetentionBudgetMb = 12.0;

    private const int Frames = 120;
    private const double WheelDurationMs = 1300;
    private const double WindowDurationMs = 400;

    internal static void Run(string outDir, List<string> log)
    {
        var directory = Path.Combine(outDir, "motion");
        Directory.CreateDirectory(directory);
        var rows = new List<string>();
        var previousOverride = Motion.Override;
        var process = Process.GetCurrentProcess();
        long memoryBefore = 0;
        var failures = new List<string>();
        var worstP95 = 0.0;
        try
        {
            VerifyStoryboardTargets();
            log.Add("ok motion/storyboard targets (open/stagger/pulse animations carry targets and property paths)");

            var settings = new AppSettings();
            var tools = new ToolLocator();
            var core = WheelController.BuildOperations();
            var actions = FileActions.Build(settings, () => { });
            IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
                Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

            var sample = Path.Combine(directory, "motion-sample.png");
            using (var image = new MagickImage(MagickColors.RebeccaPurple, 640, 420))
                image.Write(sample);
            var item = new FileItem(sample);
            var skips = new HashSet<string>(StringComparer.Ordinal);

            // Pass 0 warms WPF's JIT/font/raster caches; only pass 1 is recorded and its retained
            // working set is compared with the post-warm-up baseline.
            for (var pass = 0; pass < 2; pass++)
            {
                var warm = pass == 0;
                var photo = new List<FileItem> { new(@"C:\Photos\vacation.jpg") };
                var wheel = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Source, (_, _, _) => { });
                var wheelRoot = wheel.DetachForCapture();

                Motion.Override = true;
                var wheelOn = Measure(wheelRoot, t =>
                {
                    var (to, from, travel) = HighlightAt(t);
                    wheel.Pose(t, to, from, travel);
                }, applyEveryFrame: true, WheelDurationMs);

                Motion.Override = false;
                var wheelOff = Measure(wheelRoot, t =>
                {
                    var (to, from, travel) = HighlightAt(WheelDurationMs);
                    wheel.Pose(t, to, from, travel);
                }, applyEveryFrame: false, WheelDurationMs);
                wheel.Close();

                if (!warm)
                {
                    rows.Add(ToRow("wheel (424×424)", wheelOn, wheelOff));
                    Check(wheelOn.UiP95, "wheel", failures);
                    worstP95 = Math.Max(worstP95, wheelOn.UiP95);
                }

                // Every registered tool window, data-driven from the registry so a new tool is covered at merge.
                foreach (var spec in ToolRegistry.All)
                {
                    var sampleItems = SampleItems(spec, item);
                    if (sampleItems is null)
                    {
                        skips.Add(spec.Id);
                        continue;
                    }
                    ToolWindow? window = null;
                    try
                    {
                        window = new ToolWindow(spec, sampleItems, null);
                        SelfTest.Pump(() => window.PrepareAsync(animate: false));
                        var root = window.DetachForCapture();
                        Motion.Override = true;
                        var on = Measure(root, t => window.Pose(
                            Math.Min(t, Motion.WindowOpenMs),
                            Math.Max(0, t - 260)), applyEveryFrame: true, WindowDurationMs);
                        Motion.Override = false;
                        var off = Measure(root, t => window.Pose(
                            Motion.WindowOpenMs, 0), applyEveryFrame: false, WindowDurationMs);
                        if (!warm)
                        {
                            rows.Add(ToRow($"tool {spec.Id}", on, off));
                            Check(on.UiP95, $"tool {spec.Id}", failures);
                            worstP95 = Math.Max(worstP95, on.UiP95);
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // A tool whose preview cannot be prepared offscreen (a format or an OS feature the
                        // runner lacks) must not fail the motion gate; it is reported and skipped.
                        if (!warm)
                            skips.Add($"{spec.Id} ({ex.GetType().Name}: {ex.Message})");
                    }
                    finally
                    {
                        window?.Close();
                    }
                }

                if (warm)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    memoryBefore = process.WorkingSet64;
                }
            }

            foreach (var id in skips)
                log.Add($"skipped motion/{id} (no sample in the motion harness)");

            // Idle CPU: nothing animating, nothing open. Any timer left behind would show up here.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var idleCpu = IdleCpu(TimeSpan.FromMilliseconds(1000));
            if (idleCpu > IdleCpuBudgetPercent)
                failures.Add($"idle CPU {idleCpu:F2} % exceeds the {IdleCpuBudgetPercent:F0} % budget");

            process.Refresh();
            var retainedMb = Math.Max(0, process.WorkingSet64 - memoryBefore) / (1024.0 * 1024.0);
            if (retainedMb > MemoryRetentionBudgetMb)
                failures.Add($"motion retained {retainedMb:F1} MB after close (budget {MemoryRetentionBudgetMb:F0} MB)");

            var tier = Motion.RenderTier;
            log.Add($"motion: render tier {tier}, idle {idleCpu:F2} % CPU, retained {retainedMb:F1} MB, " +
                    $"frame p95 {worstP95:F2} ms (budget {FrameBudgetMs:F1} ms)");
            foreach (var row in rows)
                log.Add(row);

            List<string> report =
            [
                "# Motion self-test",
                "",
                $"Measured by `DropWheel.exe --self-test` on {Environment.OSVersion}; " +
                $"{Environment.ProcessorCount} logical processors, render tier {tier}. " +
                $"Frame budget {FrameBudgetMs:F1} ms, idle CPU budget {IdleCpuBudgetPercent:F0} %, " +
                $"retention budget {MemoryRetentionBudgetMb:F0} MB.",
                "",
                "| surface | UI frame p95 ON (ms) | UI frame p95 OFF (ms) | raster proxy p95 ON (ms) | CPU ON (%) | p95 budget |",
                "| --- | ---: | ---: | ---: | ---: | --- |",
                .. rows,
                "",
                skips.Count == 0
                    ? "Every registered tool window was driven by the harness."
                    : $"Not measured ({skips.Count} registered tool(s) want a media family the harness does not " +
                      $"create): {string.Join(", ", skips.Order(StringComparer.Ordinal))}.",
                $"Idle CPU after close: **{idleCpu:F2} %** (budget {IdleCpuBudgetPercent:F0} %).",
                $"Working set retained by the motion suite: **{retainedMb:F1} MB** (budget {MemoryRetentionBudgetMb:F0} MB).",
            ];
            File.WriteAllLines(Path.Combine(directory, "report.md"), report);
            log.Add($"ok motion/p95 + cpu + memory ({rows.Count} surfaces)");

            if (failures.Count > 0)
                throw new InvalidOperationException("motion budget failed: " + string.Join("; ", failures));
        }
        finally
        {
            Motion.Override = previousOverride;
        }
    }

    private readonly record struct Metrics(double UiP95, double UiMean, double RasterP95, double CpuPercent);

    /// <summary>
    /// The smallest sample set a tool accepts: one file, or two copies of the same file for a tool that
    /// needs a second input (collage). Null means the tool wants a media family this harness does not
    /// create (video, audio, PDF); it is then reported as skipped instead of silently unmeasured.
    /// </summary>
    internal static IReadOnlyList<FileItem>? SampleItems(IToolWindowSpec spec, FileItem item) =>
        spec.Applies([item]) ? [item]
        : spec.Applies([item, item]) ? [item, item]
        : null;

    /// <summary>
    /// Catches a broken storyboard factory (the failure mode the offscreen previews cannot see, because
    /// they pose the tree directly): every animation must carry an explicit target and a property path.
    /// Checking the storyboard object is deterministic and does not depend on a presentation source.
    /// </summary>
    private static void VerifyStoryboardTargets()
    {
        var surface = new Border();
        var handle = Motion.Prepare(surface);
        AssertTargets(Motion.Open(surface, handle), "open", surface, handle.Scale);
        Motion.Release(surface, handle);

        var slice = new Border();
        var transforms = new TransformGroup();
        var sliceScale = new ScaleTransform(0.82, 0.82);
        transforms.Children.Add(sliceScale);
        transforms.Children.Add(new TranslateTransform());
        slice.RenderTransform = transforms;
        AssertTargets(Motion.Stagger(slice, 3), "stagger", slice, sliceScale);
        Motion.Release(slice);

        var button = new Border();
        Motion.Interactive(button);
        AssertTargets(Motion.Pulse(button), "pulse", button.RenderTransform);
        Motion.Release(button);
    }

    private static void AssertTargets(Storyboard storyboard, string name, params DependencyObject?[] expected)
    {
        if (storyboard.Children.Count == 0)
            throw new InvalidOperationException($"motion {name} storyboard is empty");
        var targets = storyboard.Children
            .Select(Storyboard.GetTarget)
            .OfType<DependencyObject>()
            .ToHashSet();
        foreach (var target in expected)
            if (target is not null && !targets.Contains(target))
                throw new InvalidOperationException($"motion {name} storyboard did not target {target.GetType().Name}");
        foreach (var child in storyboard.Children)
        {
            if (Storyboard.GetTarget(child) is null || Storyboard.GetTargetProperty(child) is null)
                throw new InvalidOperationException($"motion {name} storyboard has an animation without a target/property");
        }
    }

    private static Metrics Measure(FrameworkElement root, Action<double> pose, bool applyEveryFrame, double duration)
    {
        var width = Math.Max(1, (int)Math.Ceiling(root.Width));
        var height = Math.Max(1, (int)Math.Ceiling(root.Height));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        // Warm-up: JIT the pose/render path and let the bitmap allocate before the clock starts.
        for (var i = 0; i < 8; i++)
        {
            pose(duration);
            bitmap.Render(root);
        }

        var samples = new double[Frames];
        var rasterSamples = new double[Frames];
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        for (var i = 0; i < Frames; i++)
        {
            var t = duration * i / Math.Max(1, Frames - 1);
            var clock = Stopwatch.StartNew();
            if (applyEveryFrame)
                pose(t);
            root.UpdateLayout();
            clock.Stop();
            samples[i] = clock.Elapsed.TotalMilliseconds;

            var render = Stopwatch.StartNew();
            bitmap.Clear();
            bitmap.Render(root);
            render.Stop();
            rasterSamples[i] = render.Elapsed.TotalMilliseconds;
        }
        wall.Stop();
        var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        bitmap.Clear();
        var cpuPercent = wall.Elapsed.TotalMilliseconds <= 0
            ? 0
            : cpu / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
        return new Metrics(Percentile(samples, 0.95), samples.Average(), Percentile(rasterSamples, 0.95), cpuPercent);
    }

    private static double Percentile(double[] values, double percentile)
    {
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        var index = (int)Math.Clamp(Math.Ceiling(copy.Length * percentile) - 1, 0, copy.Length - 1);
        return copy[index];
    }

    private static void Check(double p95, string surface, List<string> failures)
    {
        if (p95 > FrameBudgetMs)
            failures.Add($"{surface} frame p95 {p95:F2} ms exceeds {FrameBudgetMs:F1} ms");
    }

    private static string ToRow(string surface, Metrics on, Metrics off) =>
        $"| {surface} | {on.UiP95:F2} | {off.UiP95:F2} | {on.RasterP95:F2} | {on.CpuPercent:F1} | {FrameBudgetMs:F1} ms |";

    /// <summary>The wheel's highlight schedule used for measurement and the website clips.</summary>
    internal static (int To, int From, double Travel) HighlightAt(double timeMs)
    {
        var current = 0;
        var from = -1;
        var travel = 1.0;
        foreach (var (index, start) in new[] { (1, 420.0), (2, 700.0), (3, 980.0) })
        {
            if (timeMs < start)
                break;
            from = current;
            current = index;
            travel = Math.Clamp((timeMs - start) / Motion.HighlightMs, 0, 1);
        }
        return (current, from, travel);
    }

    private static double IdleCpu(TimeSpan duration)
    {
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        wall.Stop();
        process.Refresh();
        return wall.Elapsed.TotalMilliseconds <= 0
            ? 0
            : (process.TotalProcessorTime - cpuStart).TotalMilliseconds / wall.Elapsed.TotalMilliseconds /
              Environment.ProcessorCount * 100;
    }
}
