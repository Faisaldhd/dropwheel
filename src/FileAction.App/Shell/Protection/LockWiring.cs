using System.IO;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The app-facing Lock entry points, kept in one place so the wiring can be tested without a WPF window:
/// command-line open, startup recovery, exit relock, Settings, and the self-test step.
/// </summary>
internal static class LockWiring
{
    public static Func<string[], bool> TryHandleArgs { get; set; } = LockCli.TryHandle;

    public static Action StartupRecovery { get; set; } = LockCli.RunStartupRecovery;

    public static Action RelockOnExit { get; set; } = LockRuntime.RelockOnExit;

    public static Action OpenSettings { get; set; } = LockSettingsWindow.ShowSingle;

    public static Action<string, List<string>> SelfTest { get; set; } = LockSelfTest.Run;

    public static bool HandleArgs(string[] args) => TryHandleArgs(args);

    public static void Startup() => StartupRecovery();

    public static void Exit() => RelockOnExit();

    public static void Settings() => OpenSettings();

    public static void RunSelfTest(string outDir, List<string> log) => SelfTest(outDir, log);
}
