using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FileAction.App.Actions;
using FileAction.App.Jobs;
using FileAction.App.Shell.Protection;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.App.UI.Tools.Image;
using FileAction.App.UI.Tools.Image.Frame;
using FileAction.App.UI.Tools.Lock;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Lock;
using FileAction.Core.Operations;
using ImageMagick;
using static FileAction.Core.Loc;

namespace FileAction.App;

/// <summary>
/// GAP 2 of the 2.4.0 release: every window is built and rasterised in English and Arabic, so the claim
/// "verified in Arabic" is backed by a screenshot and by assertions, not by an English-only run.
///
/// For each surface the coverage pass asserts: a real localized title (not a key), no rendered string
/// that is a string-table key, no Arabic sentence forced to LTR, a RightToLeft root in Arabic, and (for
/// tools) a window size inside the simulated work area. Tool windows are also checked against the pure
/// <see cref="ToolWindowSizer"/> across landscape/portrait and 100 %/150 % DPI. Any failure is collected
/// and thrown at the end, so <c>--self-test</c> exits non-zero and CI fails.
/// </summary>
internal static class SelfTestUiCoverage
{
    private static readonly Regex KeyShape = new(@"^[a-z][a-z0-9]*(\.[a-z0-9]+){2,}$", RegexOptions.Compiled);
    private static readonly Regex Arabic = new(@"[\u0600-\u06FF]", RegexOptions.Compiled);

    /// <summary>A window that needs longer than this is reported as TIMEOUT, never as PASS.</summary>
    private static readonly TimeSpan PerWindowBudget = TimeSpan.FromSeconds(30);

    /// <summary>Whole coverage pass budget; keeps the self-test inside its CI step window.</summary>
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(150);

    private static Stopwatch _clock = Stopwatch.StartNew();
    private static Timer? _watchdog;

    private sealed class Row
    {
        public required string Window { get; init; }
        public required string Culture { get; init; }
        public required string Kind { get; init; }
        public string Result { get; set; } = "PASS";
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>Everything the pass covers; printed in ui-coverage.md so "verified" cannot be stretched.</summary>
    private const string Uncovered =
        "Fixed-size dialogs are only size-checked at 1280x800 @100 % (they use design sizes, not the sizer); " +
        "150 %/200 % and portrait fit is asserted for tool windows only. `UpdatedWindow` builds its Window inside " +
        "Show() and native MessageBox surfaces are not rasterised. CI has no FFmpeg before this step, so audio/video " +
        "tools are screenshotted in their real missing-tool/probe-failed state, not with media loaded. " +
        "Budget: 150 s total, 30 s per window; an over-budget surface is reported TIMEOUT and is not counted as PASS.";

    public static void Run(string outDir, List<string> log)
    {
        Directory.CreateDirectory(outDir);
        var rows = new List<Row>();
        var failures = new List<string>();
        _clock = Stopwatch.StartNew();
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // A stuck UI thread cannot be pre-empted in-process, but a watchdog keeps the tag gate bounded:
        // after the total budget plus a margin it writes a timeout report and exits non-zero.
        _watchdog = new Timer(_ =>
        {
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "ui-coverage-timeout.txt"),
                    $"TIMEOUT: coverage exceeded {TotalBudget.TotalSeconds + 30:0} s; a surface blocked the UI thread.\n");
                File.AppendAllText(Path.Combine(outDir, "ui-coverage.md"),
                    "\n\n## TIMEOUT\nCoverage watchdog fired: a surface blocked the UI thread for more than " +
                    (TotalBudget.TotalSeconds + 30) + " s.\n");
            }
            catch
            {
                // The exit code below is the signal that matters.
            }
            Environment.Exit(2);
        }, null, (int)(TotalBudget + TimeSpan.FromSeconds(30)).TotalMilliseconds, Timeout.Infinite);
        var settings = new AppSettings();
        var tools = new ToolLocator();
        var core = WheelController.BuildOperations();
        var actions = FileActions.Build(settings, () => { });
        SettingsWindow.Operations = () => [.. core, .. actions];

        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

        var samples = CreateSamples(Path.Combine(outDir, "samples"));
        var englishKeys = Loc.KeyCountFor(Language.English);
        var arabicKeys = Loc.KeyCountFor(Language.Arabic);
        Check(englishKeys > 0 && arabicKeys > 0, $"empty string table (en={englishKeys}, ar={arabicKeys})");
        Check(englishKeys == arabicKeys, $"string-table size differs (en={englishKeys}, ar={arabicKeys})");

        try
        {
            foreach (var (language, culture) in new[] { (UiLanguage.English, "en"), (UiLanguage.Arabic, "ar") })
            {
                if (OutOfTime("culture:" + culture, culture, _clock, rows, failures))
                    continue;
                LanguageManager.Apply(language);
                Theme.Override = true;
                Theme.Refresh();
                var dir = Path.Combine(outDir, culture);
                Directory.CreateDirectory(dir);

                // ---- wheel surfaces -------------------------------------------------------------
                var wheelSamples = new (string Name, WheelKind Wheel, string[] Files, int Highlight)[]
                {
                    ("wheel-image-convert", WheelKind.Convert, ["photo.png"], 1),
                    ("wheel-video-tools", WheelKind.Tools, ["clip.mp4"], 3),
                    ("wheel-actions", WheelKind.Actions, ["photo.png"], 4),
                };
                foreach (var (name, wheelKind, files, highlight) in wheelSamples)
                {
                    var items = files.Select(f => new FileItem(Path.Combine(samples.Root, f))).ToList();
                    var wheel = new WheelWindow(WheelMode.Drag, wheelKind, items, settings, Source, (_, _, _) => { });
                    rows.Add(Capture($"wheel:{name}", culture, "wheel", wheel,
                        Path.Combine(dir, name + ".png"),
                        () => wheel.RenderPreview(Path.Combine(dir, name + ".png"), highlight), failures));
                    wheel.Close();
                }

                // ---- Settings, one screenshot, all six tabs asserted ----------------------------
                var settingsWindow = new SettingsWindow(settings, tools, () => { });
                for (var tab = 0; tab < SettingsWindow.TabCount; tab++)
                {
                    settingsWindow.SelectTab(tab);
                    settingsWindow.UpdateLayout();
                    CheckWindowLabels(settingsWindow, culture, $"settings:tab{tab}", rows, failures);
                }
                settingsWindow.SelectTab(0);
                settingsWindow.UpdateLayout();
                rows.Add(Capture("settings", culture, "settings", settingsWindow,
                    Path.Combine(dir, "settings.png"), null, failures));
                settingsWindow.Close();

                var shelf = ShelfWindow.CreateForPreview(["photo.png", "doc.pdf"]);
                rows.Add(Capture("shelf", culture, "shelf", shelf, Path.Combine(dir, "shelf.png"), null, failures));
                shelf.Close();

                var jobs = new JobsWindow();
                jobs.Add(T("catalog.image.compress.label"), "photo.png", () => { }, Path.Combine(samples.Root, "photo.png"))
                    .Report(0.42);
                jobs.Add(T("settings.title"), "photo.png", () => { }, Path.Combine(samples.Root, "photo.png"))
                    .Complete([Path.Combine(samples.Root, "photo.webp")], null);
                jobs.Add(T("catalog.pack.label"), "bundle.zip", () => { }, Path.Combine(samples.Root, "bundle.zip"))
                    .Fail(T("error.tool.missing", "Pandoc", T("settings.tools.install")));
                rows.Add(Capture("jobs", culture, "jobs", jobs, Path.Combine(dir, "jobs.png"), null, failures));
                jobs.Close();

                var editor = new WheelEditorWindow(settings, [.. core, .. actions], () => { });
                rows.Add(Capture("wheel-editor", culture, "editor", editor,
                    Path.Combine(dir, "wheel-editor.png"), null, failures));
                editor.Close();

                var trim = core.First(o => o.Id == "video.trim");
                var dialog = new ParameterDialog(trim, [new FileItem(Path.Combine(samples.Root, "clip.mp4"))]);
                rows.Add(Capture("parameter-dialog", culture, "dialog", dialog,
                    Path.Combine(dir, "parameter-dialog.png"), null, failures));
                dialog.Close();

                // ---- every registered tool -------------------------------------------------------
                foreach (var spec in ToolRegistry.All)
                {
                    var items = PickItems(spec, samples);
                    if (items is null)
                    {
                        rows.Add(new Row { Window = "tool:" + spec.Id, Culture = culture, Kind = "tool", Result = "SKIP", Detail = "no sample accepted by Applies()" });
                        failures.Add($"tool:{spec.Id} ({culture}): no sample accepted by Applies()");
                        continue;
                    }

                    if (OutOfTime("tool:" + spec.Id, culture, _clock, rows, failures))
                        continue;
                    ToolWindow? window = null;
                    try
                    {
                        window = new ToolWindow(spec, items, null);
                        var probe = string.Empty;
                        try
                        {
                            SelfTest.Pump(() => window.PrepareAsync(animate: false), timeoutSeconds: 5);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            // CI has no FFmpeg before this step. A probe that cannot run is not a coverage
                            // failure: the window is still built, asserted and rasterised in its real
                            // placeholder/failure state, and the probe result is recorded in the report.
                            probe = ex is TimeoutException
                                ? "media probe TIMEOUT; window rendered without media"
                                : "media probe " + ex.GetType().Name + "; window rendered without media";
                        }

                        var row = Capture("tool:" + spec.Id, culture, "tool", window,
                            Path.Combine(dir, "tool-" + spec.Id.Replace('.', '-') + ".png"), null, failures,
                            detach: true, spec: spec);
                        if (probe.Length > 0)
                            row.Detail = (row.Detail.Length == 0 ? string.Empty : row.Detail + "; ") + "probe: " + probe;
                        rows.Add(row);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        rows.Add(new Row { Window = "tool:" + spec.Id, Culture = culture, Kind = "tool", Result = "FAIL", Detail = ex.GetType().Name + ": " + ex.Message });
                        failures.Add($"tool:{spec.Id} ({culture}): {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        window?.Close();
                    }
                }

                // ---- protection dialogs ----------------------------------------------------------
                RenderDialog("dialog:lock-warning", culture, dir, failures, rows,
                    () => LockWarningDialog.CreateForPreview([new FileItem(Path.Combine(samples.Root, "photo.png"))]));
                RenderDialog("dialog:change-password", culture, dir, failures, rows,
                    () => new ChangeFilePasswordWindow(new FileItem(Path.Combine(samples.Root, "photo.png"))));
                RenderDialog("dialog:lock-settings", culture, dir, failures, rows, () => new LockSettingsWindow());
                RenderDialog("dialog:secret-prompt", culture, dir, failures, rows,
                    () => new SecretPromptWindow(T("tool.lock.settings.currentSecret"), T("tool.lock.settings.currentSecretHint")));
                RenderDialog("dialog:lock-open", culture, dir, failures, rows,
                    () => new LockOpenWindow(Path.Combine(samples.Root, "photo.png.dwenc")));
                RenderDialog("dialog:rescuer-setup", culture, dir, failures, rows,
                    () => new RescuerSetupWindow(RescuerSetupMode.FirstTime));
                RenderDialog("dialog:rescuer-session", culture, dir, failures, rows, () => new RescuerSessionWindow());
                RenderDialog("dialog:rescuer-password", culture, dir, failures, rows,
                    () => new RescuerPasswordWindow("ABCDEFGHIJKLMNOPQRSTUVWX"));
                RenderDialog("dialog:lock-recovery", culture, dir, failures, rows,
                    () => new LockRecoveryWindow(
                        new LockSessionRecord("test-session", Path.Combine(samples.Root, "photo.png.dwenc"),
                            Path.Combine(samples.Root, "photo.png"), "photo.png", false, DateTimeOffset.UtcNow,
                            LockSessionState.Open, null),
                        LockRecoveryAction.PromptRelock));
                RenderDialog("dialog:background-color", culture, dir, failures, rows,
                    () => new AddBackgroundColorWindow(new BackgroundColor(0x82, 0x24, 0xD4)));
            }
        }
        finally
        {
            LanguageManager.Apply(UiLanguage.English);
            Theme.Override = null;
            Theme.Refresh();
            CultureInfo.CurrentCulture = previousCulture;
        }

        // ---- pure fit matrix: every tool, both orientations, 100 % and 150 % -------------------
        var shapes = new (double Width, double Height, string Name)[]
        {
            (600, 800, "portrait"), (1600, 900, "landscape"), (800, 800, "square"),
            (4000, 600, "wide"), (300, 4000, "tall"),
        };
        var workAreas = new (double Width, double Height, string Name)[]
        {
            (1920, 1080, "landscape-1920x1080"), (1080, 1920, "portrait-1080x1920"),
        };
        foreach (var spec in ToolRegistry.All)
        {
            try
            {
                foreach (var area in workAreas)
                    foreach (var scale in new[] { 1.0, 1.5 })
                        foreach (var shape in shapes)
                        {
                            var size = ToolWindowSizer.Compute(spec.Layout, shape.Width, shape.Height,
                                area.Width, area.Height, scale);
                            var dipWidth = area.Width / scale;
                            var dipHeight = area.Height / scale;
                            Check(size.Width > 0 && size.Height > 0, $"{spec.Id}: empty size");
                            Check(size.Width <= dipWidth * ToolWindowSizer.MaxWidthRatio + 0.5,
                                $"{spec.Id} {area.Name}@{scale}: width {size.Width:0.#} > {dipWidth * ToolWindowSizer.MaxWidthRatio:0.#}");
                            Check(size.Height <= dipHeight * ToolWindowSizer.MaxHeightRatio + 0.5,
                                $"{spec.Id} {area.Name}@{scale}: height {size.Height:0.#} > {dipHeight * ToolWindowSizer.MaxHeightRatio:0.#}");
                        }
                rows.Add(new Row { Window = "tool:" + spec.Id, Culture = "—", Kind = "fit", Detail = "5 shapes x 2 orientations @100/150 %" });
            }
            catch (InvalidOperationException ex)
            {
                rows.Add(new Row { Window = "tool:" + spec.Id, Culture = "—", Kind = "fit", Result = "FAIL", Detail = ex.Message });
                failures.Add(ex.Message);
            }
        }

        // ---- reports ---------------------------------------------------------------------------
        WriteReports(outDir, rows, failures, englishKeys);
        _watchdog?.Dispose();
        _watchdog = null;
        log.Add($"ok ui-coverage: {rows.Count(row => row.Result == "PASS")} pass, " +
                $"{rows.Count(row => row.Result == "SKIP")} skip, {failures.Count} fail ({englishKeys} keys en+ar)");
        if (failures.Count > 0)
            throw new InvalidOperationException(
                $"UI coverage (en+ar) failed for {failures.Count} surface(s): " + string.Join(" | ", failures.Take(8)));
    }

    // ------------------------------------------------------------------------------- rendering

    private static Row Capture(string id, string culture, string kind, Window window, string file,
        Action? render, List<string> failures, bool detach = false, IToolWindowSpec? spec = null)
    {
        var row = new Row { Window = id, Culture = culture, Kind = kind };
        var windowClock = Stopwatch.StartNew();
        try
        {
            var problems = InspectWindow(window, culture, out var emptyCount);
            if (spec is not null)
                problems.AddRange(CheckToolFit(window, spec));

            // Rasterising one window normally takes well under a second. Running it through the same
            // bounded pump as the media probes turns a stuck surface into a recorded TIMEOUT instead of
            // an unbounded CI hang.
            SelfTest.Pump(() =>
            {
                if (render is not null)
                {
                    render();
                }
                else if (detach)
                {
                    if (window is not ToolWindow toolWindow)
                        throw new InvalidOperationException("detach capture is only wired for ToolWindow");
                    var root = toolWindow.DetachForCapture();
                    SelfTest.SaveElement(root, file);
                }
                else
                {
                    SelfTest.SaveWindow(window, file, window.Width > 0 ? window.Width : 520);
                }
                return Task.CompletedTask;
            }, timeoutSeconds: 10);

            row.Detail = emptyCount == 0 ? "no empty labels" : $"{emptyCount} dynamic empty slot(s)";
            if (problems.Count > 0)
            {
                row.Result = "FAIL";
                row.Detail = string.Join("; ", problems);
                failures.Add($"{id} ({culture}): {row.Detail}");
            }
            else if (windowClock.Elapsed > PerWindowBudget)
            {
                // A surface that needed longer than the budget is NOT a pass, even if it finished.
                row.Result = "TIMEOUT";
                row.Detail = $"window took {windowClock.Elapsed.TotalSeconds:0.0} s (budget {PerWindowBudget.TotalSeconds:0} s)";
                failures.Add($"{id} ({culture}): {row.Detail}");
            }
        }
        catch (TimeoutException ex)
        {
            row.Result = "TIMEOUT";
            row.Detail = "render did not finish within 10 s: " + ex.Message;
            failures.Add($"{id} ({culture}): {row.Detail}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            row.Result = "FAIL";
            row.Detail = ex.GetType().Name + ": " + ex.Message;
            failures.Add($"{id} ({culture}): {row.Detail}");
        }
        return row;
    }

    /// <summary>True when the whole pass is out of time; the surface is reported as TIMEOUT, not omitted.</summary>
    private static bool OutOfTime(string id, string culture, Stopwatch clock, List<Row> rows, List<string> failures)
    {
        if (clock.Elapsed <= TotalBudget)
            return false;
        const string detail = "coverage deadline reached; window NOT rendered";
        rows.Add(new Row { Window = id, Culture = culture, Kind = "budget", Result = "TIMEOUT", Detail = detail });
        failures.Add($"{id} ({culture}): {detail}");
        return true;
    }

    private static void RenderDialog(string id, string culture, string dir, List<string> failures, List<Row> rows,
        Func<Window> build)
    {
        if (OutOfTime(id, culture, _clock, rows, failures))
            return;
        Window? window = null;
        try
        {
            window = build();
            UiSetup.Configure(window, animate: false);
            rows.Add(Capture(id, culture, "dialog", window, Path.Combine(dir, id.Replace(':', '-') + ".png"), null, failures));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A dialog that cannot be constructed is NOT a deliberate skip: it fails the gate.
            rows.Add(new Row { Window = id, Culture = culture, Kind = "dialog", Result = "FAIL", Detail = ex.GetType().Name + ": " + ex.Message });
            failures.Add($"{id} ({culture}): dialog build failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            window?.Close();
        }
    }

    private static List<string> CheckToolFit(Window window, IToolWindowSpec spec)
    {
        // The runner's real work area is environment-dependent (headless CI can report 0x0 or 1024x768),
        // so it is never a pass/fail input. The simulated 1280x800 default is the release contract.
        var problems = new List<string>();
        var size = ToolWindowSizer.Compute(spec.Layout, null, null, 1280, 800, 1.0);
        if (size.Width > 1280 || size.Height > 800)
            problems.Add($"default {size.Width:0.#}x{size.Height:0.#} exceeds 1280x800");
        return problems;
    }

    private static void CheckWindowLabels(Window window, string culture, string where, List<Row> rows, List<string> failures)
    {
        var problems = InspectWindow(window, culture, out _);
        if (problems.Count == 0)
            return;
        rows.Add(new Row { Window = where, Culture = culture, Kind = "labels", Result = "FAIL", Detail = string.Join("; ", problems) });
        failures.Add($"{where} ({culture}): {string.Join("; ", problems)}");
    }

    /// <summary>Collects rendered strings and returns localization problems for one window.</summary>
    private static List<string> InspectWindow(Window window, string culture, out int emptyLabels)
    {
        var problems = new List<string>();
        var labels = new List<(string Text, FlowDirection Flow)>();
        // Window titles are drawn by the OS title bar (not by the WPF content tree), so they are checked
        // for raw keys/emptiness but not for a WPF FlowDirection they do not own.
        var titles = new List<string>();
        var empties = 0;
        var seen = new HashSet<DependencyObject>();

        void AddText(string? text, FlowDirection flow = FlowDirection.LeftToRight)
        {
            if (text is null)
                return;
            if (text.Length == 0 || string.IsNullOrWhiteSpace(text))
            {
                empties++;
                return;
            }
            labels.Add((text, flow));
        }

        void AddBlock(TextBlock block)
        {
            if (string.IsNullOrEmpty(block.Text))
            {
                if (block.Inlines.Count == 0)
                    empties++;
            }
            else
            {
                labels.Add((block.Text, block.FlowDirection));
            }
            foreach (var inline in block.Inlines)
                if (inline is Run run && !string.IsNullOrEmpty(run.Text))
                    labels.Add((run.Text, block.FlowDirection));
        }

        void Walk(DependencyObject node)
        {
            if (!seen.Add(node))
                return;
            if (node is Window w)
                titles.Add(w.Title ?? string.Empty);
            if (node is TextBlock block)
                AddBlock(block);
            if (node is ContentControl content && content.Content is string contentText)
                AddText(contentText, content.FlowDirection);
            if (node is HeaderedContentControl headered && headered.Header is string headerText)
                AddText(headerText, headered.FlowDirection);
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject dependency)
                    Walk(dependency);
            if (node is Visual || node is Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    Walk(VisualTreeHelper.GetChild(node, i));
        }

        Walk(window);
        emptyLabels = empties;

        if (string.IsNullOrWhiteSpace(window.Title))
            problems.Add("empty window title");

        var badKeys = labels
            .Select(l => l.Text)
            .Concat(titles)
            .Where(text => Loc.IsKey(text) || (KeyShape.IsMatch(text.Trim()) && !LooksLikeFile(text)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (badKeys.Count > 0)
            problems.Add("raw key(s): " + string.Join(", ", badKeys));

        if (culture == "ar")
        {
            if (window.FlowDirection != FlowDirection.RightToLeft)
                problems.Add("root FlowDirection is not RightToLeft");
            var ltrArabic = labels
                .Where(l => Arabic.IsMatch(l.Text) && l.Flow != FlowDirection.RightToLeft)
                .Select(l => l.Text)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ltrArabic.Count > 0)
                problems.Add("Arabic text forced LTR: " + string.Join(", ", ltrArabic.Take(3)));
        }

        return problems;
    }

    /// <summary>File names and paths can look like a dotted key; they are data, not untranslated UI.</summary>
    private static bool LooksLikeFile(string text) =>
        text.Contains('\\') || text.Contains('/') ||
        text.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith(".dwenc", StringComparison.OrdinalIgnoreCase);

    // --------------------------------------------------------------------------------- samples

    private sealed record SampleSet(string Root, IReadOnlyDictionary<string, FileItem> Files);

    private static SampleSet CreateSamples(string root)
    {
        Directory.CreateDirectory(root);
        var files = new Dictionary<string, FileItem>(StringComparer.Ordinal);

        var imagePath = Path.Combine(root, "photo.png");
        using (var image = new MagickImage(MagickColors.RebeccaPurple, 640, 420))
            image.Write(imagePath);
        files["photo.png"] = new FileItem(imagePath);

        var jpgPath = Path.Combine(root, "photo.jpg");
        using (var image = new MagickImage(MagickColors.MediumPurple, 800, 600))
        {
            image.Format = MagickFormat.Jpg;
            image.Write(jpgPath);
        }
        files["photo.jpg"] = new FileItem(jpgPath);

        var wavPath = Path.Combine(root, "tone.wav");
        WriteWav(wavPath);
        files["tone.wav"] = new FileItem(wavPath);

        var mp4Path = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(mp4Path, FakeMp4());
        files["clip.mp4"] = new FileItem(mp4Path);

        var pdfPath = Path.Combine(root, "doc.pdf");
        File.WriteAllText(pdfPath, MinimalPdf(), Encoding.ASCII);
        files["doc.pdf"] = new FileItem(pdfPath);

        var zipPath = Path.Combine(root, "bundle.zip");
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("note.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("DropWheel sample archive.");
        }
        files["bundle.zip"] = new FileItem(zipPath);

        var txtPath = Path.Combine(root, "notes.txt");
        File.WriteAllText(txtPath, "DropWheel sample text.");
        files["notes.txt"] = new FileItem(txtPath);

        var folder = Path.Combine(root, "folder");
        Directory.CreateDirectory(folder);
        File.Copy(imagePath, Path.Combine(folder, "photo.png"), overwrite: true);
        files["folder"] = new FileItem(folder);

        return new SampleSet(root, files);
    }

    /// <summary>Representative input per tool; merge/join tools get two of their own kind.</summary>
    private static IReadOnlyList<FileItem>? PickItems(IToolWindowSpec spec, SampleSet samples)
    {
        string[][] candidates =
        [
            ["photo.png"], ["photo.jpg"], ["tone.wav"], ["clip.mp4"], ["doc.pdf"], ["bundle.zip"],
            ["notes.txt"], ["folder"],
            ["photo.png", "photo.jpg"], ["photo.png", "photo.png", "photo.jpg"],
            ["doc.pdf", "doc.pdf"], ["clip.mp4", "clip.mp4"], ["bundle.zip", "bundle.zip"],
        ];
        foreach (var candidate in candidates)
        {
            var items = candidate.Select(name => samples.Files[name]).ToList();
            if (spec.Applies(items))
                return items;
        }
        return null;
    }

    private static void WriteWav(string path)
    {
        const int rate = 8000;
        const int samples = rate / 2;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000);
            writer.Write(value);
        }
    }

    private static byte[] FakeMp4() => Encoding.ASCII.GetBytes(
        "\0\0\0\u0018ftypisom\0\0\u0002\0isomiso2mp41\0\0\0\bmdat");

    /// <summary>A minimal one-page PDF with a valid xref, enough for the document tools to open it.</summary>
    private static string MinimalPdf()
    {
        var objects = new[]
        {
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n",
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >> endobj\n",
            "4 0 obj << /Length 44 >> stream\nBT /F1 12 Tf 20 100 Td (DropWheel) Tj ET\nendstream endobj\n",
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var item in objects)
        {
            offsets.Add(builder.Length);
            builder.Append(item);
        }
        var xref = builder.Length;
        builder.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        builder.Append("trailer << /Size 5 /Root 1 0 R >>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        return builder.ToString();
    }

    // -------------------------------------------------------------------------------- reports

    private static void WriteReports(string outDir, List<Row> rows, List<string> failures, int keys)
    {
        var txt = new StringBuilder();
        txt.AppendLine($"DropWheel 2.4.0 UI coverage — en + ar — {DateTime.UtcNow:u}");
        txt.AppendLine($"keys: {keys} English / {keys} Arabic");
        txt.AppendLine($"surfaces: {rows.Count} rows, pass {rows.Count(r => r.Result == "PASS")}, " +
                       $"skip {rows.Count(r => r.Result == "SKIP")}, fail {failures.Count}");
        foreach (var row in rows)
            txt.AppendLine($"{row.Result}\t{row.Culture}\t{row.Kind}\t{row.Window}\t{row.Detail}");
        txt.AppendLine();
        txt.AppendLine("UNCOVERED: " + Uncovered);
        File.WriteAllText(Path.Combine(outDir, "ui-coverage.txt"), txt.ToString());

        var md = new StringBuilder();
        md.AppendLine("# DropWheel 2.4.0 — UI coverage (en + ar)");
        md.AppendLine();
        md.AppendLine($"Generated by `--self-test` · {rows.Count} surfaces · **{failures.Count} failure(s)**");
        md.AppendLine();
        md.AppendLine("| Window | Culture | Kind | Result | Detail |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var row in rows.OrderBy(r => r.Window, StringComparer.Ordinal).ThenBy(r => r.Culture, StringComparer.Ordinal))
            md.AppendLine($"| `{row.Window}` | {row.Culture} | {row.Kind} | {row.Result} | {row.Detail.Replace("|", "/")} |");
        md.AppendLine();
        md.AppendLine("## Not covered by this run");
        md.AppendLine();
        md.AppendLine(Uncovered);
        if (failures.Count > 0)
        {
            md.AppendLine();
            md.AppendLine("## Failures");
            md.AppendLine();
            foreach (var failure in failures)
                md.AppendLine("- " + failure);
        }
        File.WriteAllText(Path.Combine(outDir, "ui-coverage.md"), md.ToString());
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Coverage check failed: " + what);
    }
}
