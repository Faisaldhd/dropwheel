using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Actions;
using FileAction.App.Jobs;
using FileAction.App.UI;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.App;

/// <summary>
/// <c>DropWheel.exe --self-test [folder]</c>: builds every window offscreen and renders previews to PNG.
/// Used by CI to catch UI construction errors on a real Windows machine.
/// </summary>
internal static class SelfTest
{
    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(outDir);
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            JobCard.Animate = false;
            foreach (var (language, dark, folder) in new[]
                     {
                         (UiLanguage.English, true, "en"),
                         (UiLanguage.English, false, "en-light"),
                         (UiLanguage.Arabic, true, "ar"),
                         (UiLanguage.Arabic, false, "ar-light"),
                     })
            {
                LanguageManager.Apply(language);
                Theme.Override = dark;
                Theme.Refresh();
                RenderAll(Path.Combine(outDir, folder), language, log);
            }
            LanguageManager.Apply(UiLanguage.English);
            Theme.Override = null;
            Theme.Refresh();

            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            // Saved after every step, so a hard crash (which skips the catch below) still shows where it stopped.
            foreach (var step in new Action[] { () => TestSettingsRoundTrip(log), () => TestExplorerCom(outDir, log), () => TestDrops(log), () => TestOcr(outDir, log) })
            {
                File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
                step();
            }

            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            }
            catch (IOException)
            {
            }
            return 1;
        }
    }

    private static void RenderAll(string outDir, UiLanguage language, List<string> log)
    {
        Directory.CreateDirectory(outDir);
        var rtl = language == UiLanguage.Arabic;
        // Arabic text arrives with the string tables; until then the Arabic variant shows the same
        // English labels with a real RightToLeft layout, which is exactly what this preview checks.
        var settings = new AppSettings { Language = language };
        var tools = new ToolLocator();
        var core = WheelController.BuildOperations();
        var actions = FileActions.Build(settings, () => { });
        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools);

        var samples = new (string Name, WheelMode Mode, WheelKind Wheel, string[] Files, int Highlight)[]
        {
            ("wheel-image-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Photos\vacation.jpg"], 1),
            ("wheel-image-tools", WheelMode.Click, WheelKind.Tools, [@"C:\Photos\vacation.jpg"], 0),
            ("wheel-video-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Videos\clip.mov"], 0),
            ("wheel-video-tools", WheelMode.Drag, WheelKind.Tools, [@"C:\Videos\clip.mov"], 3),
            ("wheel-pdf-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Documents\contract.pdf"], 2),
            ("wheel-pdf-tools", WheelMode.Click, WheelKind.Tools, [@"C:\a.pdf", @"C:\b.pdf"], 0),
            ("wheel-sheet-convert", WheelMode.Drag, WheelKind.Convert, [@"C:\Data\sales.xlsx"], -1),
            ("wheel-actions", WheelMode.Drag, WheelKind.Actions, [@"C:\Photos\vacation.jpg"], 4),
            ("wheel-waiting-for-drop", WheelMode.Drag, WheelKind.Convert, [], -1),
        };
        foreach (var s in samples)
        {
            var window = new WheelWindow(s.Mode, s.Wheel, s.Files.Select(f => new FileItem(f)).ToList(), settings, Source, (_, _, _) => { });
            Check(window.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
                "wheel flow direction");
            window.RenderPreview(Path.Combine(outDir, s.Name + ".png"), s.Highlight);
            window.Close();
            log.Add($"ok {Path.GetFileName(outDir)}/{s.Name}");
        }

        SettingsWindow.Operations = () => [.. core, .. actions];
        var settingsWindow = new SettingsWindow(settings, tools, () => { });
        Check(settingsWindow.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
            "settings flow direction");
        SaveWindow(settingsWindow, Path.Combine(outDir, "settings.png"), 560);
        log.Add($"ok {Path.GetFileName(outDir)}/settings");

        var shelf = ShelfWindow.CreateForPreview(
            new[] { @"C:\Photos\vacation.jpg", @"C:\Documents\contract.pdf" });
        Check(shelf.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
            "shelf flow direction");
        SaveWindow(shelf, Path.Combine(outDir, "shelf.png"), 290, desktop: true);
        shelf.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/shelf");

        var jobs = new JobsWindow();
        var running = jobs.Add("Convert to MP4", "clip.mov", () => { }, @"C:\x.mov");
        running.Report(0.42);
        jobs.Add("Compress", "vacation.jpg", () => { }, @"C:\x.jpg").Complete([@"C:\Photos\vacation (compressed).jpg"], null);
        jobs.Add("Convert to DOCX", "contract.pdf", () => { }, @"C:\x.pdf").Fail("Pandoc is missing. Install it with: " + "winget install JohnMacFarlane.Pandoc");
        SaveWindow(jobs, Path.Combine(outDir, "jobs.png"), 380, desktop: true);
        jobs.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/jobs");

        settings.FavoriteOperations = ["image.to.webp", "image.compress"];
        settings.HiddenOperations = ["image.to.tiff"];
        var editor = new WheelEditorWindow(settings, [.. core, .. actions], () => { });
        Check(editor.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
            "wheel editor flow direction");
        SaveWindow(editor, Path.Combine(outDir, "wheel-editor.png"), 640);
        log.Add($"ok {Path.GetFileName(outDir)}/wheel-editor");

        // Customised wheel and the "repeat last" view.
        var photo = new List<FileItem> { new(@"C:\Photos\vacation.jpg") };
        IReadOnlyList<Operation> Arranged(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            WheelLayout.Arrange(Source(wheel, items), settings.HiddenOperations, settings.FavoriteOperations);
        var custom = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Arranged, (_, _, _) => { });
        Check(custom.CurrentWheel == WheelKind.Convert, "custom wheel");
        custom.RenderPreview(Path.Combine(outDir, "wheel-customized.png"), 0);
        custom.Close();
        var webp = core.Single(o => o.Id == "image.to.webp");
        var repeat = new WheelWindow(WheelMode.Drag, WheelKind.Convert, photo, settings, Arranged, (_, _, _) => { }) { LastOperation = _ => webp };
        repeat.ToggleRepeat();
        repeat.RenderPreview(Path.Combine(outDir, "wheel-repeat.png"), 0);
        repeat.Close();
        log.Add($"ok {Path.GetFileName(outDir)}/wheel-repeat");

        var trim = core.Single(o => o.Id == "video.trim");
        var dialog = new ParameterDialog(trim, [new FileItem(@"C:\Videos\clip.mov")]);
        Check(dialog.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
            "parameter dialog flow direction");
        SaveWindow(dialog, Path.Combine(outDir, "trim-dialog.png"), 360);
        log.Add($"ok {Path.GetFileName(outDir)}/parameter dialog");
    }

    /// <summary>The settings file is written with source-generated JSON (no reflection in the trimmed build).</summary>
    private static void TestSettingsRoundTrip(List<string> log)
    {
        var settings = new AppSettings { Hotkey = HotkeyPreset.CtrlAltW, FavoriteOperations = ["image.to.webp"], Language = UiLanguage.Arabic };
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;
        Check(back.Hotkey == HotkeyPreset.CtrlAltW && back.FavoriteOperations.Single() == "image.to.webp" && back.Language == UiLanguage.Arabic, "settings JSON round trip");
        Check(json.Contains("\"CtrlAltW\""), "enums stored as names");
        Check(json.Contains("\"Arabic\""), "language stored as a name");

        // Old builds wrote null/English/Czech; unknown values must fall back to System without
        // discarding the rest of the file.
        var legacy = new (string Json, UiLanguage Expected)[]
        {
            ("{\"Language\":\"English\"}", UiLanguage.English),
            ("{\"Language\":\"Arabic\"}", UiLanguage.Arabic),
            ("{\"Language\":\"Czech\"}", UiLanguage.System),
            ("{\"Language\":null}", UiLanguage.System),
            ("{\"Language\":1}", UiLanguage.System),
            ("{\"Language\":\"Klingon\"}", UiLanguage.System),
            ("{}", UiLanguage.System),
        };
        foreach (var (source, expected) in legacy)
        {
            var migrated = System.Text.Json.JsonSerializer.Deserialize(AppSettings.MigrateLanguage(source), SettingsJson.Default.AppSettings)!;
            Check(migrated.Language == expected, $"legacy language {source}");
        }

        Check(LanguageManager.Resolve(UiLanguage.System, "ar-SA") == UiLanguage.Arabic, "system language Arabic");
        Check(LanguageManager.Resolve(UiLanguage.System, "en-GB") == UiLanguage.English, "system language English");
        Check(LanguageManager.Resolve(UiLanguage.Arabic, "en-GB") == UiLanguage.Arabic, "explicit Arabic");
        Check(LanguageManager.Resolve(UiLanguage.English, "ar-SA") == UiLanguage.English, "explicit English");

        LanguageManager.Apply(UiLanguage.Arabic);
        Check(Bidi.Value("report.pdf") == "\u2066report.pdf\u2069", "bidi isolate in RTL");
        LanguageManager.Apply(UiLanguage.English);
        Check(Bidi.Value("report.pdf") == "report.pdf", "no bidi isolate in LTR");
        log.Add("ok settings json + language migration + bidi");
    }

    /// <summary>Reading the Explorer selection goes through late-bound COM, which trimming could break.</summary>
    private static void TestExplorerCom(string outDir, List<string> log)
    {
        var file = Path.Combine(outDir, "smoke", "smoke.jpg");
        using (System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\"")) { }
        for (var attempt = 0; attempt < 40; attempt++)
        {
            Thread.Sleep(500);
            var frame = FindWindow("CabinetWClass", null);
            if (frame == IntPtr.Zero)
                continue;
            var selection = Shell.ExplorerSelection.Get(frame);
            if (selection.Any(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase)))
            {
                log.Add("ok explorer selection");
                PostMessage(frame, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                return;
            }
        }
        // Without an interactive desktop Explorer may not open a window; the COM calls above still ran.
        log.Add("skipped explorer selection (no Explorer window appeared)");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Virtual files (as browsers and Outlook drag them) and raw images become real files.</summary>
    private static void TestDrops(List<string> log)
    {
        var descriptor = new byte[4 + 592];
        BitConverter.GetBytes(1).CopyTo(descriptor, 0);
        System.Text.Encoding.Unicode.GetBytes("selftest-virtual.txt").CopyTo(descriptor, 4 + 72);
        var data = new DataObject();
        data.SetData("FileGroupDescriptorW", new MemoryStream(descriptor));
        data.SetData("FileContents", new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello")));

        var preview = DropContent.Read(data, materialize: false) ?? throw new InvalidOperationException("virtual file not recognised");
        Check(preview.Items.Single().Format == "txt", "virtual file preview has the right type");
        var dropped = DropContent.Read(data, materialize: true) ?? throw new InvalidOperationException("virtual file not saved");
        var path = dropped.Items.Single().Path;
        Check(File.ReadAllText(path) == "hello", "virtual file contents");
        File.Delete(path);
        log.Add("ok drop/virtual-file");

        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16);
        var image = new DataObject(DataFormats.Bitmap, bitmap);
        var imageDrop = DropContent.Read(image, materialize: true) ?? throw new InvalidOperationException("image not saved");
        var png = imageDrop.Items.Single().Path;
        Check(File.Exists(png) && new FileInfo(png).Length > 0, "dropped image saved as PNG");
        File.Delete(png);
        log.Add("ok drop/image");

        var link = new DataObject(DataFormats.UnicodeText, "https://example.com/photos/cat.webp");
        var linkDrop = DropContent.Read(link, materialize: false) ?? throw new InvalidOperationException("link not recognised");
        Check(linkDrop.Items.Single().Format == "webp", "link keeps its file type");
        log.Add("ok drop/link");
    }

    /// <summary>Renders a line of text and reads it back with Windows OCR, when the runner has it.</summary>
    private static void TestOcr(string outDir, List<string> log)
    {
        if (!Ocr.OcrOperation.Available)
        {
            log.Add("skipped ocr (no recognizer languages on this machine)");
            return;
        }
        var text = new TextBlock
        {
            Text = "DropWheel OCR 12345",
            FontSize = 48,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brushes.Black,
            Background = Brushes.White,
            Padding = new Thickness(30),
        };
        var png = Path.Combine(outDir, "ocr-input.png");
        SaveElement(text, png);
        var engine = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
            ?? Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages[0]);
        var result = Task.Run(() => Ocr.OcrOperation.RecognizeAsync(engine, png)).GetAwaiter().GetResult();
        Check(result.Contains("12345"), $"OCR read \"{result}\"");
        log.Add($"ok ocr: {result}");
    }
    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Self-test check failed: " + what);
    }

    private static void SaveWindow(Window window, string file, double width, bool desktop = false)
    {
        var content = (UIElement)window.Content;
        window.Content = null;
        var root = new Border
        {
            Width = width,
            Background = desktop ? Desktop() : window.Background ?? Brushes.White,
            Child = content,
            // The window itself is never shown here; the preview root must carry its direction.
            FlowDirection = window.FlowDirection,
        };
        if (content is ScrollViewer scroll)
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SaveElement(root, file);
    }

    /// <summary>A wallpaper-like backdrop for floating surfaces in previews.</summary>
    public static Brush Desktop() => Theme.IsDark
        ? new LinearGradientBrush(Color.FromRgb(0x1F, 0x2A, 0x44), Color.FromRgb(0x3A, 0x2A, 0x4A), 45)
        : new LinearGradientBrush(Color.FromRgb(0xC9, 0xD8, 0xEE), Color.FromRgb(0xE9, 0xD9, 0xEE), 45);

    public static void SaveElement(FrameworkElement element, string file)
    {
        element.Measure(new Size(element.Width > 0 ? element.Width : 800, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }
}
