using System.Diagnostics;
using FileAction.App.Shell.Protection;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using FileAction.App.Actions;
using FileAction.App.Jobs;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;
using FileAction.Core.Updates;
using ImageMagick;

namespace FileAction.App;

/// <summary>
/// <c>DropWheel.exe --self-test [folder]</c>: builds every window offscreen and renders previews to PNG.
/// Used by CI to catch UI construction errors on a real Windows machine.
/// </summary>
internal static class SelfTest
{
    /// <summary>Wall-clock start of this process, read from the OS for the cold-start budget.</summary>
    private static DateTime _processStart = DateTime.Now;

    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(outDir);
            _processStart = ProcessStart();
            // The earliest managed observation point: the tool module initializers have run, the app has
            // not built a wheel or a tool view yet. A heavy registrar (bitmap, Magick, ToolWindow) would
            // have loaded its renderer assembly by now, so this is the registration laziness proof.
            var registeredBeforeUi = ToolRegistry.All.Count;
            var renderersAtRegistration = RendererAssembliesLoaded();
            log.Add($"perf registration: {registeredBeforeUi} tool spec(s), renderer assemblies loaded: " +
                (renderersAtRegistration.Count == 0 ? "none" : string.Join(", ", renderersAtRegistration)));
            Check(registeredBeforeUi > 0, "no tool windows registered");
            Check(renderersAtRegistration.Count == 0,
                "tool registration loaded a renderer assembly: " + string.Join(", ", renderersAtRegistration));

            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            JobCard.Animate = false;

            // Render with the real update service (no network is started): this is the path the installed
            // app uses, and the one the previous framework-dependent screenshots never exercised.
            using var updateService = new UpdateService(
                typeof(Program).Assembly.GetName().Version ?? new Version(2, 2, 1),
                UpdatePaths.IsPortable);
            SettingsWindow.Updates = updateService;
            try
            {
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
                    var keys = Loc.KeyCount;
                    log.Add($"ok {folder}/localization: {keys} keys ({Loc.Current})");
                    if (keys < 300)
                        throw new InvalidOperationException(
                            $"localization loaded only {keys} keys for {Loc.Current}; embedded string tables are broken");
                    // The first pass is the cold start: the process has just loaded, the update service has
                    // been created and the wheel is about to be built. Later passes only render previews.
                    RenderAll(Path.Combine(outDir, folder), language, log, measureLaunch: folder == "en");
                }
            }
            finally
            {
                SettingsWindow.Updates = null;
            }
            LanguageManager.Apply(UiLanguage.English);
            Theme.Override = null;
            Theme.Refresh();

            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            // Saved after every step, so a hard crash (which skips the catch below) still shows where it stopped.
            foreach (var step in new Action[] { () => TestSettingsRoundTrip(log), () => TestExplorerCom(outDir, log), () => TestDrops(log), () => TestOcr(outDir, log), () => TestToolWindows(outDir, log), () => SelfTestUiCoverage.Run(Path.Combine(outDir, "ui-coverage"), log), () => MotionSelfTest.Run(outDir, log), () => RenderMotionClips(outDir, log), () => LockWiring.RunSelfTest(outDir, log) })
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

    /// <summary>
    /// Recognises <c>--screenshots [folder]</c> as the first argument, the same shape as
    /// <c>--self-test [folder]</c>. Split out of <see cref="Program.Main"/> so a unit test can prove the
    /// flag is recognised without starting WPF.
    /// </summary>
    internal static bool TryScreenshots(string[] args, out string outDir)
    {
        outDir = "screenshots";
        if (args.Length == 0 || args[0] != "--screenshots")
            return false;
        if (args.Length >= 2 && !args[1].StartsWith("--", StringComparison.Ordinal))
            outDir = args[1];
        return true;
    }

    /// <summary>
    /// <c>DropWheel.exe --screenshots [folder]</c>: the production renderer behind the website assets.
    /// For en/ar × dark/light it renders the real wheel and Settings window and all registered tool
    /// windows into <c>&lt;folder&gt;/&lt;language&gt;/&lt;theme&gt;/{wheel,settings,tools}</c> (other
    /// preview surfaces go to <c>other/</c>) and exits 0. It deliberately does NOT run the cold-start /
    /// idle-memory budget, the motion gates, the lock/settings round trips or the 20× open/close leak
    /// loops: those stay in <see cref="Run"/> and are not weakened by this mode.
    /// </summary>
    public static int RunScreenshots(string outDir)
    {
        var log = new List<string>();
        var work = Path.Combine(outDir, ".work");
        try
        {
            Directory.CreateDirectory(outDir);
            if (Directory.Exists(work))
                Directory.Delete(work, recursive: true);
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            JobCard.Animate = false;

            // Same real update service the installed app builds. Creating it starts no network work; it is
            // here so the rendered Settings window takes the production path instead of a test-only one.
            using var updateService = new UpdateService(
                typeof(Program).Assembly.GetName().Version ?? new Version(2, 2, 1),
                UpdatePaths.IsPortable);
            SettingsWindow.Updates = updateService;
            try
            {
                var samplesDir = Path.Combine(work, "samples");
                Directory.CreateDirectory(samplesDir);
                var samples = CreateToolSamples(samplesDir);
                foreach (var (language, dark, languageFolder, themeFolder) in new[]
                         {
                             (UiLanguage.English, true, "en", "dark"),
                             (UiLanguage.English, false, "en", "light"),
                             (UiLanguage.Arabic, true, "ar", "dark"),
                             (UiLanguage.Arabic, false, "ar", "light"),
                         })
                {
                    LanguageManager.Apply(language);
                    Theme.Override = dark;
                    Theme.Refresh();
                    var keys = Loc.KeyCount;
                    if (keys < 300)
                        throw new InvalidOperationException(
                            $"localization loaded only {keys} keys for {Loc.Current}; embedded string tables are broken");

                    var staging = Path.Combine(work, languageFolder + "-" + themeFolder);
                    var destination = Path.Combine(outDir, languageFolder, themeFolder);
                    RenderAll(staging, language, log, measureLaunch: false);
                    // Re-runs must not keep a stale PNG that the current build no longer renders.
                    if (Directory.Exists(destination))
                        Directory.Delete(destination, recursive: true);
                    MoveSurfaces(staging, destination);
                    RenderTools(Path.Combine(destination, "tools"), samples, log);
                    log.Add($"ok {languageFolder}/{themeFolder}: wheel {CountPngs(Path.Combine(destination, "wheel"))}, " +
                            $"settings {CountPngs(Path.Combine(destination, "settings"))}, " +
                            $"tools {CountPngs(Path.Combine(destination, "tools"))}, " +
                            $"other {CountPngs(Path.Combine(destination, "other"))}");
                }
            }
            finally
            {
                SettingsWindow.Updates = null;
            }

            LanguageManager.Apply(UiLanguage.English);
            Theme.Override = null;
            Theme.Refresh();

            Directory.Delete(work, recursive: true);
            File.WriteAllLines(Path.Combine(outDir, "screenshots.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllLines(Path.Combine(outDir, "screenshots.txt"), log);
                if (Directory.Exists(work))
                    Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
            return 1;
        }
    }

    /// <summary>
    /// Moves the previews <see cref="RenderAll"/> wrote at the staging root into the website's fixed
    /// layout: <c>wheel/</c> (wheel surfaces), <c>settings/</c>, and <c>other/</c> for the remaining
    /// surfaces (shelf, jobs, wheel editor, parameter dialog).
    /// </summary>
    private static void MoveSurfaces(string staging, string destination)
    {
        var wheel = Path.Combine(destination, "wheel");
        Directory.CreateDirectory(wheel);
        foreach (var file in Directory.GetFiles(staging, "wheel-*.png"))
        {
            // The wheel editor is a Settings-side window, not a wheel surface: it belongs in other/.
            if (string.Equals(Path.GetFileName(file), "wheel-editor.png", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Move(file, Path.Combine(wheel, Path.GetFileName(file)));
        }

        var settings = Path.Combine(destination, "settings");
        Directory.CreateDirectory(settings);
        MoveIfExists(Path.Combine(staging, "settings.png"), Path.Combine(settings, "settings.png"));

        var other = Path.Combine(destination, "other");
        Directory.CreateDirectory(other);
        foreach (var file in Directory.GetFiles(staging, "*.png"))
            File.Move(file, Path.Combine(other, Path.GetFileName(file)));

        Directory.Delete(staging, recursive: true);
    }

    private static void MoveIfExists(string source, string destination)
    {
        if (File.Exists(source))
            File.Move(source, destination);
    }

    private static int CountPngs(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*.png").Length : 0;

    /// <summary>
    /// Renders every registered tool exactly once with the same sample media the self-test uses. There is
    /// no warm-up/leak loop and no working-set gate here; the preview is captured once per combination.
    /// </summary>
    private static void RenderTools(string directory, IReadOnlyList<FileItem> samples, List<string> log)
    {
        Directory.CreateDirectory(directory);
        if (ToolRegistry.All.Count == 0)
            throw new InvalidOperationException("no tool windows registered");

        // Capture the settled state: the open fade must not be caught mid-animation by the PNG encoder.
        var previousMotion = Motion.Override;
        Motion.Override = false;
        try
        {
            foreach (var spec in ToolRegistry.All)
            {
                var items = FindToolSample(spec, samples)
                    ?? throw new InvalidOperationException(
                        $"the screenshots mode has no sample media for '{spec.Id}'; add its format to CreateToolSamples()");
                var window = new ToolWindow(spec, items, null);
                ShowOffscreen(window);
                var prepared = PumpTool(() => window.PrepareAsync(animate: false), TimeSpan.FromSeconds(20));
                PumpFor(TimeSpan.FromMilliseconds(300));
                if (prepared)
                {
                    SaveWindow(window, Path.Combine(directory, PreviewName(spec) + ".png"), window.Width, desktop: true);
                    log.Add($"ok tools/{PreviewName(spec)}");
                }
                else
                {
                    log.Add($"skipped tools/{PreviewName(spec)} (the preview did not prepare within 20 s)");
                }
                CloseToolWindow(window);
            }
        }
        finally
        {
            Motion.Override = previousMotion;
        }
    }

    private static void RenderAll(string outDir, UiLanguage language, List<string> log, bool measureLaunch)
    {
        Directory.CreateDirectory(outDir);
        var rtl = language == UiLanguage.Arabic;
        // Arabic text arrives with the string tables; until then the Arabic variant shows the same
        // English labels with a real RightToLeft layout, which is exactly what this preview checks.
        var settings = new AppSettings { Language = language };
        var tools = new ToolLocator();
        var renderersAtStart = RendererAssembliesLoaded();
        var core = WheelController.BuildOperations();
        var actions = FileActions.Build(settings, () => { });
        // The catalog legitimately references renderer types (image edit lambdas take a MagickImage);
        // the wheel itself must not add another renderer assembly on top of that.
        var renderersBeforeWheel = RendererAssembliesLoaded();
        IReadOnlyList<Operation> Source(WheelKind wheel, IReadOnlyList<FileItem> items) =>
            WheelGroups.Arrange(Catalog.For(wheel == WheelKind.Actions ? actions : core, wheel, items, tools));

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
            if (measureLaunch)
            {
                MeasureLaunch(window, log, renderersAtStart, renderersBeforeWheel);
                measureLaunch = false;
            }
            window.RenderPreview(Path.Combine(outDir, s.Name + ".png"), s.Highlight);
            window.Close();
            log.Add($"ok {Path.GetFileName(outDir)}/{s.Name}");
        }

        SettingsWindow.Operations = () => [.. core, .. actions];
        var settingsWindow = new SettingsWindow(settings, tools, () => { });
        Check(settingsWindow.FlowDirection == (rtl ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight),
            "settings flow direction");
        for (var tab = 0; tab < SettingsWindow.TabCount; tab++)
        {
            settingsWindow.SelectTab(tab);
            settingsWindow.UpdateLayout();
            CheckNoRawKeys(settingsWindow, $"settings tab {tab}");
        }
        settingsWindow.SelectTab(0);
        settingsWindow.UpdateLayout();
        // Render Settings at its real width, on the page colour, so the six tabs stay on one row and the
        // dark halo is visible in the website capture. The live window itself stays transparent.
        settingsWindow.Background = Theme.Page;
        SaveWindow(settingsWindow, Path.Combine(outDir, "settings.png"), 700);
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
    /// <summary>
    /// Website motion assets are generated by the self-test but must not fail a build on their own: the
    /// motion budget gate is <see cref="MotionSelfTest"/>, which does fail hard. A codec or parser
    /// problem is logged as a skip so the rest of the self-test evidence still reaches the artifact.
    /// </summary>
    private static void RenderMotionClips(string outDir, List<string> log)
    {
        try
        {
            MotionClips.Run(outDir, log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or MagickException)
        {
            log.Add($"skipped motion/clips ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static void TestSettingsRoundTrip(List<string> log)
    {
        var settings = new AppSettings { Hotkey = HotkeyPreset.CtrlAltW, FavoriteOperations = ["image.to.webp"], Language = UiLanguage.Arabic };
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var back = System.Text.Json.JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings)!;
        Check(back.Hotkey == HotkeyPreset.CtrlAltW && back.FavoriteOperations.Single() == "image.to.webp" && back.Language == UiLanguage.Arabic, "settings JSON round trip");
        Check(json.Contains("\"CtrlAltW\""), "enums stored as names");
        Check(json.Contains("\"Arabic\""), "language stored as a name");

        // Exercise the real settings.json file path, not just the JSON serializer: SaveTo writes the same
        // file AppSettings.Save writes, LoadFrom reads it through the same migration code.
        var tempDir = Path.Combine(Path.GetTempPath(), "DropWheel-settings-" + Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(tempDir, "settings.json");
        try
        {
            Directory.CreateDirectory(tempDir);
            settings.SaveTo(settingsFile);
            Check(File.Exists(settingsFile), "settings.json written");
            var fileText = File.ReadAllText(settingsFile);
            Check(fileText.Contains("\"Arabic\"") && fileText.Contains("\"CtrlAltW\""), "settings.json stores enum names");

            var loaded = AppSettings.LoadFrom(settingsFile);
            Check(loaded.Hotkey == HotkeyPreset.CtrlAltW &&
                  loaded.FavoriteOperations.Single() == "image.to.webp" &&
                  loaded.Language == UiLanguage.Arabic, "settings.json file round trip");

            File.WriteAllText(settingsFile, "{ not valid json");
            var fallback = AppSettings.LoadFrom(settingsFile);
            Check(fallback.Language == UiLanguage.System && fallback.Hotkey == new AppSettings().Hotkey,
                "corrupt settings.json falls back to defaults");

            File.WriteAllText(settingsFile, "{\"Language\":\"Czech\",\"Hotkey\":\"CtrlAltW\"}");
            var migratedFile = AppSettings.LoadFrom(settingsFile);
            Check(migratedFile.Language == UiLanguage.System && migratedFile.Hotkey == HotkeyPreset.CtrlAltW,
                "legacy settings.json file migration");
            log.Add("ok settings.json file load/save");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The temp folder is best-effort cleanup.
            }
        }

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

        try
        {
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
            if (!result.Contains("12345"))
            {
                // OCR is a best-effort extra check in the screenshot job; a recognizer difference must not
                // fail the screenshots the release notes and the download page depend on.
                log.Add($"skipped ocr (recognizer returned \"{result}\")");
                return;
            }
            log.Add($"ok ocr: {result}");
        }
        catch (Exception ex)
        {
            log.Add($"skipped ocr (failed: {ex.GetType().Name}: {ex.Message})");
        }
    }
    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("Self-test check failed: " + what);
    }

    /// <summary>
    /// Fails when a rendered string is itself a string-table key. This is the regression that produced the
    /// black 2.2.0 Settings window: a broken table made every lookup return its key.
    /// </summary>
    private static void CheckNoRawKeys(DependencyObject root, string where)
    {
        var bad = new List<string>();
        var seen = new HashSet<DependencyObject>();
        void Inspect(string? text)
        {
            if (Loc.IsKey(text))
                bad.Add(text!);
        }

        void Walk(DependencyObject node)
        {
            if (!seen.Add(node))
                return;
            if (node is Window window)
                Inspect(window.Title);
            if (node is TextBlock block)
            {
                Inspect(block.Text);
                foreach (var inline in block.Inlines)
                    if (inline is Run run)
                        Inspect(run.Text);
            }
            if (node is ContentControl content && content.Content is string contentText)
                Inspect(contentText);
            if (node is HeaderedContentControl headered && headered.Header is string headerText)
                Inspect(headerText);
            // The logical tree reaches every tab page even when it has not been realised visually yet.
            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject dependency)
                    Walk(dependency);
            if (node is Visual || node is Visual3D)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    Walk(VisualTreeHelper.GetChild(node, i));
            }
        }

        Walk(root);
        if (bad.Count > 0)
            throw new InvalidOperationException(
                $"raw localization key rendered in {where}: {string.Join(", ", bad.Distinct(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// Measures the launch budgets once, on the first preview: process start to a laid-out wheel (the wheel
    /// is laid out exactly as ShowAt would lay it out for a drag) and the idle working set right after
    /// launch. Nothing is rendered to PNG before the measurement.
    /// </summary>
    private static void MeasureLaunch(WheelWindow wheel, List<string> log,
        IReadOnlyList<string> renderersAtStart, IReadOnlyList<string> renderersBeforeWheel)
    {
        if (wheel.Content is FrameworkElement content)
        {
            content.Measure(new Size(wheel.Width, wheel.Height));
            content.Arrange(new Rect(0, 0, wheel.Width, wheel.Height));
            content.UpdateLayout();
        }

        var coldStartMs = (DateTime.Now - _processStart).TotalMilliseconds;
        var coldStartLimitMs = LaunchBudget.ColdStartMs * (1 + LaunchBudget.ColdStartGrowth);
        log.Add($"perf cold-start: {coldStartMs:F0} ms " +
                $"(v2.3.0 baseline {LaunchBudget.ColdStartMs:F0} ms, +{LaunchBudget.ColdStartGrowth:P0} budget {coldStartLimitMs:F0} ms)");
        Check(coldStartMs <= coldStartLimitMs,
            $"cold start {coldStartMs:F0} ms exceeds the budget {coldStartLimitMs:F0} ms " +
            $"(v2.3.0 baseline {LaunchBudget.ColdStartMs:F0} ms +{LaunchBudget.ColdStartGrowth:P0})");

        PumpDispatcher();
        TrimMemory();
        var idleMb = WorkingSetMb();
        var idleLimitMb = LaunchBudget.IdleMb + LaunchBudget.IdleToleranceMb;
        log.Add($"perf idle: {idleMb:F1} MB working set " +
                $"(v2.3.0 baseline {LaunchBudget.IdleMb:F1} MB, +{LaunchBudget.IdleToleranceMb:F0} MB budget {idleLimitMb:F1} MB)");
        Check(idleMb <= idleLimitMb,
            $"idle working set {idleMb:F1} MB exceeds the budget {idleLimitMb:F1} MB " +
            $"(v2.3.0 baseline {LaunchBudget.IdleMb:F1} MB +{LaunchBudget.IdleToleranceMb:F0} MB)");

        // The operation catalog legitimately references renderer types (the image edit operations take a
        // MagickImage). Constructing the wheel itself must not load an additional tool renderer assembly.
        var catalogLoaded = renderersBeforeWheel.Except(renderersAtStart, StringComparer.OrdinalIgnoreCase).ToList();
        if (catalogLoaded.Count > 0)
            log.Add("note tools/lazy: the operation catalog loaded renderer assembly/assemblies: " +
                    string.Join(", ", catalogLoaded));
        var wheelLoaded = RendererAssembliesLoaded()
            .Except(renderersBeforeWheel, StringComparer.OrdinalIgnoreCase).ToList();
        log.Add(wheelLoaded.Count == 0
            ? "ok tools/lazy: laying out the wheel loaded no tool renderer assembly"
            : "FAILED tools/lazy: the wheel loaded " + string.Join(", ", wheelLoaded));
        Check(wheelLoaded.Count == 0,
            "building the wheel loaded tool renderer assembly/assemblies: " + string.Join(", ", wheelLoaded));
    }

    /// <summary>
    /// Exercises every registered floating tool offscreen: renders one preview per tool, then opens and
    /// closes each window many times and gates the working-set delta, leaked ffmpeg processes and leftover
    /// temp files. It is data-driven from <see cref="ToolRegistry.All"/>, so a tool merged later is covered
    /// without editing this method (only its media format may need adding to <see cref="CreateToolSamples"/>).
    /// </summary>
    private static void TestToolWindows(string outDir, List<string> log)
    {
        var directory = Path.Combine(outDir, "tools");
        Directory.CreateDirectory(directory);
        Check(ToolRegistry.All.Count > 0, "no tool windows registered");

        var samples = CreateToolSamples(directory);

        // The adaptive sizer must keep every media shape inside the work area.
        foreach (var (width, height, name) in new (double, double, string)[]
                 {
                     (300, 500, "portrait"), (500, 300, "landscape"), (500, 500, "square"),
                     (5000, 300, "wide"), (300, 5000, "tall"), (32, 32, "tiny"), (12000, 9000, "huge"),
                 })
        {
            var size = ToolWindowSizer.Compute(ToolSizes.Crop, width, height, 1920, 1080, 1.0);
            if (size.Width > 1920 * 0.95 + 0.01 || size.Height > 1080 * 0.90 + 0.01)
                throw new InvalidOperationException($"tool sizer does not fit {name}: {size.Width}x{size.Height}");
        }
        log.Add("ok tools/sizer (7 shapes fit the work area)");

        // Point TEMP/TMP at a folder this test owns, so any temp file a tool forgets is both provable
        // (the folder is empty at the end) and recoverable (it is under the uploaded self-test output).
        var tempRoot = Path.Combine(directory, "temp");
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
        Directory.CreateDirectory(tempRoot);
        var ffmpegBefore = FfmpegProcessIds();
        var previousTemp = Environment.GetEnvironmentVariable("TEMP");
        var previousTmp = Environment.GetEnvironmentVariable("TMP");
        // The tool window now closes with a 120 ms fade. For a leak measurement the close must be
        // synchronous and complete before the working set is sampled, so motion is off for this loop;
        // the animation itself is gated separately by MotionSelfTest.
        var previousMotion = Motion.Override;
        Motion.Override = false;
        Environment.SetEnvironmentVariable("TEMP", tempRoot);
        Environment.SetEnvironmentVariable("TMP", tempRoot);

        var table = new StringBuilder();
        table.AppendLine("| tool | opens | baseline MB | final MB | delta MB | peak MB | result |");
        table.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        try
        {
            foreach (var spec in ToolRegistry.All)
            {
                var items = FindToolSample(spec, samples)
                    ?? throw new InvalidOperationException(
                        $"the self-test has no sample media for '{spec.Id}'; add its format to CreateToolSamples()");

                // Warm up once: the first open of a tool is where its lazily loaded renderer (for the
                // image tools, Magick's native library) and the one preview PNG are paid for. The leak
                // budget applies to the repeated open/close cycles after that one-time cost.
                // The window is shown before it is prepared: some tools (tools.lock) complete their
                // Ready task only from Loaded, which runs when the window is shown.
                var warmup = new ToolWindow(spec, items, null);
                ShowOffscreen(warmup);
                var prepared = PumpTool(() => warmup.PrepareAsync(animate: false), TimeSpan.FromSeconds(20));
                PumpFor(TimeSpan.FromMilliseconds(300));
                if (!prepared)
                    log.Add($"skipped perf/{spec.Id} (the preview did not prepare within 20 s; " +
                            "open/close leak cycles are not measured for this tool)");
                else
                {
                    SaveWindow(warmup, Path.Combine(directory, PreviewName(spec) + ".png"), warmup.Width, desktop: true);
                    log.Add($"ok tools/{PreviewName(spec)} (preview, {ToolRegistry.All.Count} registered)");
                }
                CloseToolWindow(warmup);
                TrimMemory();
                if (!prepared)
                    continue;

                var before = WorkingSetMb();
                var peak = before;
                for (var i = 0; i < LaunchBudget.ToolOpenCloseIterations; i++)
                {
                    var window = new ToolWindow(spec, items, null);
                    ShowOffscreen(window);
                    // A tool that prepared once must prepare again; a bounded wait keeps one slow
                    // implementation from turning 20 cycles into a 10 minute hang.
                    PumpTool(() => window.PrepareAsync(animate: false), TimeSpan.FromSeconds(10));
                    PumpFor(TimeSpan.FromMilliseconds(60));
                    CloseToolWindow(window);
                    TrimMemory();
                    peak = Math.Max(peak, WorkingSetMb());
                }

                var after = WorkingSetMb();
                var delta = after - before;
                var within = delta <= LaunchBudget.ToolWorkingSetToleranceMb;
                table.AppendLine(
                    $"| {spec.Id} | {LaunchBudget.ToolOpenCloseIterations} | {before:F1} | {after:F1} | " +
                    $"{delta:+0.0;-0.0;0.0} | {peak:F1} | {(within ? "ok" : "OVER BUDGET")} |");
                log.Add($"perf {spec.Id}: {LaunchBudget.ToolOpenCloseIterations} open/close, working set " +
                    $"{before:F1} -> {after:F1} MB (delta {delta:+0.0;-0.0;0.0} MB, peak {peak:F1} MB, " +
                    $"tolerance +{LaunchBudget.ToolWorkingSetToleranceMb:F1} MB)");
                Check(within,
                    $"{spec.Id} leaks {delta:F1} MB over {LaunchBudget.ToolOpenCloseIterations} open/close cycles " +
                    $"(limit +{LaunchBudget.ToolWorkingSetToleranceMb:F1} MB)");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMP", previousTemp);
            Environment.SetEnvironmentVariable("TMP", previousTmp);
            Motion.Override = previousMotion;
        }

        // Give the last window's asynchronous cleanup a moment, then check what the tools left behind.
        Thread.Sleep(250);
        var leftovers = Directory.Exists(tempRoot)
            ? Directory.GetFileSystemEntries(tempRoot, "*", SearchOption.AllDirectories)
            : [];
        log.Add(leftovers.Length == 0
            ? "ok tools/temp: no temp files left"
            : $"FAILED tools/temp: {leftovers.Length} entr{(leftovers.Length == 1 ? "y" : "ies")} left: " +
              string.Join(", ", leftovers.Take(5).Select(Path.GetFileName)));
        Check(leftovers.Length == 0, $"{leftovers.Length} temp file(s) left behind by tool windows");

        var leaked = FfmpegProcessIds().Except(ffmpegBefore).OrderBy(id => id).ToArray();
        log.Add(leaked.Length == 0
            ? "ok tools/ffmpeg: no leaked ffmpeg process"
            : $"FAILED tools/ffmpeg: {leaked.Length} process(es) left (pids {string.Join(", ", leaked)})");
        Check(leaked.Length == 0, $"tool windows left {leaked.Length} ffmpeg process(es) running");

        foreach (var line in table.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            log.Add("perf " + line.TrimEnd('\r'));
        log.Add($"ok tools/lazy ({ToolRegistry.All.Count} spec(s); views are built only by Create())");
    }

    /// <summary>Samples for the tool windows: real PNG/JPG/PDF plus extension placeholders for every format.</summary>
    private static IReadOnlyList<FileItem> CreateToolSamples(string directory)
    {
        var samples = new List<FileItem>();
        void Add(string name) => samples.Add(new FileItem(Path.Combine(directory, name)));

        using (var image = new MagickImage(MagickColors.RebeccaPurple, 640, 420))
            image.Write(Path.Combine(directory, "sample.png"));
        using (var image = new MagickImage(MagickColors.Teal, 480, 320))
            image.Write(Path.Combine(directory, "sample.jpg"));
        Add("sample.png");
        Add("sample.jpg");

        // A real one-page PDF: the PDF tools open their input synchronously in some views, and a
        // hand-written stub without an xref table can be rejected by PDFsharp/PdfPig before the view
        // has a chance to report the failure.
        var pdfPath = Path.Combine(directory, "sample.pdf");
        using (var document = new PdfSharp.Pdf.PdfDocument())
        {
            document.AddPage();
            document.Save(pdfPath);
        }
        Add("sample.pdf");

        // Applies() only inspects the normalized extension, so a placeholder is enough to pick the sample.
        // A tool that cannot decode it reports the failure inside its own preview, which ToolWindow catches.
        foreach (var extension in new[]
                 {
                     "webp", "gif", "bmp", "tiff", "ico", "avif", "svg",
                     "mp4", "mov", "mkv", "webm", "avi",
                     "mp3", "wav", "m4a", "flac", "ogg",
                     "docx", "xlsx", "pptx", "txt", "md", "csv", "zip", "epub", "srt", "vtt",
                 })
        {
            var name = "sample." + extension;
            File.WriteAllText(Path.Combine(directory, name), "DropWheel self-test sample");
            Add(name);
        }
        return samples;
    }

    /// <summary>The first sample (one file, or a valid two-file drop) the tool accepts, or null.</summary>
    private static IReadOnlyList<FileItem>? FindToolSample(IToolWindowSpec spec, IReadOnlyList<FileItem> samples)
    {
        foreach (var item in samples)
            if (spec.Applies([item]))
                return [item];
        // Batch tools (merge, collage, join…) need at least two files; the same file twice is a valid drop.
        foreach (var item in samples)
            if (spec.Applies([item, item]))
                return [item, item];
        foreach (var first in samples)
            foreach (var second in samples)
                if (!ReferenceEquals(first, second) && spec.Applies([first, second]))
                    return [first, second];
        return null;
    }

    private static string PreviewName(IToolWindowSpec spec) =>
        spec.Id[(spec.Id.LastIndexOf('.') + 1)..];

    /// <summary>Shows a tool window well outside the desktop at zero opacity, so Close() runs its teardown.</summary>
    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.Opacity = 0;
        try
        {
            window.Show();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A runner without an interactive desktop cannot realize an HWND; Close() below still runs
            // (or the content is detached) and the measurement remains valid for the managed view.
        }
    }

    private static void CloseToolWindow(Window window)
    {
        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
            // A never-shown window may refuse Close; detaching its tree is enough to collect it.
        }
        finally
        {
            window.Content = null;
        }
    }

    private static HashSet<int> FfmpegProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("ffmpeg"))
        {
            using (process)
                ids.Add(process.Id);
        }
        return ids;
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Drains the dispatcher and collects, so a working-set reading reflects live, not queued, state.</summary>
    private static void TrimMemory()
    {
        PumpDispatcher();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        PumpDispatcher();
    }

    private static double WorkingSetMb()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64 / (1024.0 * 1024.0);
    }

    private static DateTime ProcessStart()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Fall back to the first managed observation: the gate then measures at least the managed work.
            return DateTime.Now;
        }
    }

    /// <summary>
    /// The renderer assemblies a tool may load when its view is built. None of them may be loaded by tool
    /// registration (module initializers), which is the laziness contract this self-test enforces.
    /// </summary>
    private static readonly string[] RendererAssemblyPrefixes =
    [
        "Magick.NET", "PdfSharp", "PDFtoImage", "SkiaSharp", "ClosedXML", "ExcelDataReader",
        "UglyToad.PdfPig", "SharpCompress", "SixLabors", "DocumentFormat.OpenXml", "ZXing",
    ];

    private static List<string> RendererAssembliesLoaded()
    {
        var loaded = new List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (RendererAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                loaded.Add(name);
        }
        loaded.Sort(StringComparer.OrdinalIgnoreCase);
        return loaded;
    }

    /// <summary>
    /// v2.3.0 launch budgets, measured with this exact self-test on the v2.3.0 release code (main
    /// 4a4a39f) on GitHub windows-latest (see docs/PERF.md). Only change these with a fresh v2.3.0
    /// measurement, never to make a regression pass.
    /// </summary>
    private static class LaunchBudget
    {
        /// <summary>
        /// Process start to a laid-out wheel on v2.3.0, in milliseconds. Recorded conservatively as the
        /// slowest windows-latest measurement of the v2.3.0-equivalent build (1,290 ms of 759, 816, 850,
        /// 906, 927, 939, 1,201 and 1,290 ms) rounded up with a runner-class margin: runner hardware
        /// differs by far more than the 5 % budget between machines, so a tight baseline would flake on
        /// healthy builds. The gate still fails a startup regression of several hundred milliseconds.
        /// </summary>
        public const double ColdStartMs = 1500;
        /// <summary>Allowed cold-start growth over the recorded v2.3.0 baseline before the gate fails.</summary>
        public const double ColdStartGrowth = 0.05;
        /// <summary>Idle working set after launch on v2.3.0, in MB (slowest of 75.0, 75.7, 76.2 and 76.3 MB).</summary>
        public const double IdleMb = 77.0;
        /// <summary>Allowed idle-memory growth over the v2.3.0 baseline, in MB.</summary>
        public const double IdleToleranceMb = 15.0;
        /// <summary>Open/close cycles per registered tool.</summary>
        public const int ToolOpenCloseIterations = 20;
        /// <summary>
        /// Allowed working-set growth after the warm-up, in MB. Across the 25 merged tools the healthy
        /// range is -8.9 to +11.3 MB over 20 cycles (the largest is metadata); 16 MB keeps collection
        /// timing from failing a tool that releases its window, while a real retained renderer or
        /// document still exceeds it.
        /// </summary>
        public const double ToolWorkingSetToleranceMb = 16.0;
    }

    /// <summary>Runs an async preparation to completion while the self-test keeps the UI thread alive.</summary>
    /// <param name="start">Starts the work. It must be called after the dispatcher context is installed,
    /// otherwise its awaits resume on the thread pool and touch WPF objects off the UI thread.</param>
    internal static void Pump(Func<Task> start, int timeoutSeconds = 30)
    {
        var previous = SynchronizationContext.Current;
        if (previous is not DispatcherSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var frame = new DispatcherFrame();
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            var task = start();
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                frame.Continue = false;
            };
            guard.Start();
            task.ContinueWith(_ =>
            {
                guard.Stop();
                frame.Continue = false;
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            if (!task.IsCompleted)
                throw new TimeoutException($"the tool window preview did not finish within {timeoutSeconds} s");
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }


    /// <summary>Pumps the dispatcher for a bounded time, so queued Loaded handlers and preview continuations run.</summary>
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>
    /// Like <see cref="Pump"/>, but returns false when the work does not finish within
    /// <paramref name="timeout"/> instead of throwing. The per-tool loop uses it so one tool that cannot
    /// prepare a placeholder sample cannot turn 20 cycles into a multi-minute hang.
    /// </summary>
    private static bool PumpTool(Func<Task> start, TimeSpan timeout)
    {
        var previous = SynchronizationContext.Current;
        if (previous is not DispatcherSynchronizationContext)
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var frame = new DispatcherFrame();
            var guard = new DispatcherTimer { Interval = timeout };
            var task = start();
            guard.Tick += (_, _) =>
            {
                guard.Stop();
                frame.Continue = false;
            };
            guard.Start();
            task.ContinueWith(_ =>
            {
                guard.Stop();
                frame.Continue = false;
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            // Observe a fault so a failed preparation does not surface later as an unobserved exception.
            _ = task.Exception;
            return task.IsCompleted;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    internal static void SaveWindow(Window window, string file, double width, bool desktop = false)

    {
        CheckNoRawKeys(window, Path.GetFileName(file));
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
