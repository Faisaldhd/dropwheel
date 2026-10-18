using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileAction.Core;
using FileAction.Core.Operations;
using FileAction.Linux.UI;

namespace FileAction.Linux;

/// <summary><c>dropwheel --self-test DIR</c>: renders the windows headlessly to PNG (no display needed; used by CI).</summary>
public static class SelfTest
{
    public static int Run(string outDir)
    {
        var log = new List<string>();
        try
        {
            Program.WithBundledFont(AppBuilder.Configure<App>().UseSkia())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            foreach (var (language, dark, folder) in new[] { (Language.English, true, "en"), (Language.English, false, "en-light"), (Language.Czech, true, "cs") })
            {
                Loc.Current = language;
                Palette.Override = dark;
                RenderAll(Path.Combine(outDir, folder), language == Language.English, log);
            }
            log.AddRange(SmokeTest.Run(Path.Combine(outDir, "smoke")));
            var json = System.Text.Json.JsonSerializer.Serialize(new LinuxSettings { Language = Language.Czech }, LinuxSettingsJson.Default.LinuxSettings);
            if (System.Text.Json.JsonSerializer.Deserialize(json, LinuxSettingsJson.Default.LinuxSettings)?.Language != Language.Czech)
                throw new InvalidOperationException("settings JSON round trip");
            log.Add("ok settings json");
            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAILED: " + ex);
            Directory.CreateDirectory(outDir);
            File.WriteAllLines(Path.Combine(outDir, "self-test.txt"), log);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RenderAll(string dir, bool english, List<string> log)
    {
        Directory.CreateDirectory(dir);
        string N(string cs, string en) => english ? en : cs;
        var settings = new LinuxSettings();
        var controller = new Controller(settings, null);
        var photo = new List<FileItem> { new(N("/home/jana/Obrázky/dovolená.jpg", "/home/jane/Pictures/vacation.jpg")) };
        var video = new List<FileItem> { new(N("/home/jana/Videa/klip.mov", "/home/jane/Videos/clip.mov")) };

        Save(Wheel(controller, photo, WheelKind.Convert, 1), dir, "wheel-image-convert", log);
        Save(Wheel(controller, photo, WheelKind.Tools, 0), dir, "wheel-image-tools", log);
        Save(Wheel(controller, video, WheelKind.Convert, 0), dir, "wheel-video-convert", log);
        Save(Wheel(controller, photo, WheelKind.Actions, 2), dir, "wheel-actions", log);
        Save(Wheel(controller, [], WheelKind.Convert, -1), dir, "wheel-drop-zone", log);

        var jobs = new JobsWindow();
        jobs.Add(N("Převod na MP4", "Convert to MP4"), N("klip.mov", "clip.mov"), () => { }).Report(0.42);
        jobs.Add(N("Komprimovat", "Compress"), N("dovolená.jpg", "vacation.jpg"), () => { })
            .Complete([N("/home/jana/Obrázky/dovolená (komprimováno).jpg", "/home/jane/Pictures/vacation (compressed).jpg")], null);
        Save(jobs, dir, "jobs", log);

        var trim = LinuxOperations.Conversions(settings).Single(o => o.Id == "video.trim");
        Save(new ParameterWindow(trim, video), dir, "trim-dialog", log);
    }

    private static WheelWindow Wheel(Controller controller, List<FileItem> items, WheelKind kind, int highlight)
    {
        var wheel = controller.CreateWheel(items);
        if (kind != WheelKind.Convert)
            wheel.SwitchTo(kind);
        if (highlight >= 0)
            wheel.Highlight(highlight);
        return wheel;
    }

    private static void Save(Window window, string dir, string name, List<string> log)
    {
        Palette.Refresh();
        if (window is not WheelWindow)
            window.Background = Palette.Page;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("nothing rendered: " + name);
        #pragma warning disable CS0618 // the replacement overload needs encoder options we do not use
        frame.Save(Path.Combine(dir, name + ".png"));
#pragma warning restore CS0618
        window.Close();
        log.Add($"ok {Path.GetFileName(dir)}/{name}");
    }
}
