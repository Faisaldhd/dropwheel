using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using FileAction.Core;

namespace FileAction.Linux;

public sealed class App : Application
{
    public static IReadOnlyList<FileItem> StartupItems { get; set; } = [];

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = LinuxSettings.Load();
            var controller = new Controller(settings, desktop);
            var wheel = controller.CreateWheel(StartupItems);
            wheel.Show();
            wheel.Activate();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    public static AppBuilder BuildAvaloniaApp() =>
        WithBundledFont(AppBuilder.Configure<App>().UsePlatformDetect());

    /// <summary>
    /// Uses the bundled Inter font as the default, so the app doesn't depend on which fonts the system has
    /// (a minimal install may have none, and Avalonia then refuses to start).
    /// </summary>
    public static AppBuilder WithBundledFont(AppBuilder builder) =>
        builder.WithInterFont().With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });

    [STAThread]
    public static int Main(string[] args)
    {
        var settings = LinuxSettings.Load();
        Loc.Current = settings.Language ?? Loc.Detect();

        if (args.Length > 0 && Cli.Commands.Contains(args[0]))
            return Cli.Run(args);
        if (args.Length > 0 && args[0] == "--self-test")
            return SelfTest.Run(args.Length > 1 ? args[1] : "self-test");

        App.StartupItems = args
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => a.StartsWith("file://", StringComparison.Ordinal) ? new Uri(a).LocalPath : Path.GetFullPath(a))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p => new FileItem(p))
            .ToList();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
