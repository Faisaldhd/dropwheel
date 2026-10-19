using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileAction.App.Actions;
using FileAction.App.Diagnostics;
using FileAction.App.Input;
using FileAction.App.Jobs;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.App.Wheel;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppLog.Initialize();

        if (args.Length >= 1 && args[0] == "--self-test")
            return SelfTest.Run(args.Length >= 2 ? args[1] : "self-test");
        if (args.Contains("--diagnose"))
            return Diagnose.Run(args.SkipWhile(a => a != "--diagnose").Skip(1).FirstOrDefault());
        if (args.Contains("--unregister"))
        {
            // Called by the uninstaller.
            ShellIntegration.SetContextMenu(false);
            ShellIntegration.SetAutostart(false);
            ShellIntegration.SetSendTo(false);
            return 0;
        }

        // R-21: a low-integrity process cannot write to %APPDATA%/%TEMP% or receive input from normal windows,
        // which makes the app look like it "opens and disappears". Say so instead of failing silently.
        if (Integrity.IsLow)
        {
            AppLog.Error($"refusing to start: low-integrity process ({Integrity.Describe()})");
            MessageBox.Show(
                "DropWheel was started from a low-integrity location, so it cannot save settings or watch the " +
                "keyboard and mouse.\n\nCopy DropWheel to a normal folder (for example " +
                "%LOCALAPPDATA%\\Programs\\DropWheel) or run the installer, then start it again.\n\n" +
                "Run \"DropWheel.exe --diagnose\" for details.",
                "DropWheel", MessageBoxButton.OK, MessageBoxImage.Error);
            return 3;
        }

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Already running: hand over the request (or just open the settings).
            SingleInstance.Forward(args.Length > 0 ? args : ["--settings"]);
            return 0;
        }
        var app = new App(instance, args);
        return app.Run();
    }
}

internal sealed class App : Application
{
    private readonly SingleInstance _instance;
    private readonly string[] _args;
    private AppSettings _settings = null!;
    private ToolLocator _tools = null!;
    private GlobalInput? _input;
    private WheelController _controller = null!;
    private TrayIcon? _tray;
    private HotkeyListener? _hotkey;
    private HotkeyPreset? _activeHotkey;

    public App(SingleInstance instance, string[] args)
    {
        _instance = instance;
        _args = args;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashGuard.Install(this);

        _settings = AppSettings.Load();
        AppLog.Info($"settings loaded from {Path.Combine(AppSettings.Folder, "settings.json")}");
        LanguageManager.Apply(_settings.Language);
        AppLog.Info($"language: {LanguageManager.Current} (preference {LanguageManager.Preference}), rtl={LanguageManager.IsRtl}");
        Theme.Apply(_settings.Theme);
        _tools = new ToolLocator();
        var jobs = new JobRunner(_settings, _tools);
        SettingsWindow.DownloadTool = tool => jobs.DownloadToolsAsync([tool], ask: false);

        _input = new GlobalInput(Dispatcher);
        var hooksFailed = false;
        var hooksAllowed = Policy.HooksAllowed(_settings, _args);
        if (hooksAllowed)
        {
            try
            {
                _input.Start();
                AppLog.Info($"input hooks installed (keyboard={_input.HooksInstalled}, last Win32 error {_input.HookError})");
            }
            catch (InvalidOperationException ex)
            {
                hooksFailed = true;
                AppLog.Error($"input hooks failed (Win32 error {_input.HookError})", ex);
                Dialogs.Show(ex.Message, "DropWheel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            // R-20: company-safe default. No SetWindowsHookEx at all unless the user (or IT) allows it.
            AppLog.Info($"gesture hooks are off (setting={_settings.WatchGestures}, policy={Policy.HooksDisabled}, " +
                        $"--no-hooks={_args.Contains("--no-hooks")})");
        }

        _controller = new WheelController(_settings, _tools, jobs, _input);
        Ocr.OcrOperation.CopyToClipboard = text => Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }
        });
        _tray = CreateTray();
        AppLog.Info("tray icon created");
        if (hooksFailed)
        {
            _tray.Show("DropWheel", T("app.tray.hooksUnavailable"), true);
        }
        else if (!hooksAllowed)
        {
            // Company-safe default: explain how to use the app without any keyboard/mouse monitoring.
            _tray.Show("DropWheel", T("app.tray.gesturesOff"), false);
        }
        SettingsWindow.Operations = () => _controller.AllOperations;
        _hotkey = new HotkeyListener(_controller.OnHotkey);
        ApplyHotkey();
        AppLog.Info("hotkey applied");
        _instance.Listen(Dispatcher, HandleArgs);

        ShellIntegration.Refresh();
        if (!_settings.FirstRunDone)
        {
            _settings.FirstRunDone = true;
            _settings.Save();
            OpenSettings();
        }
        else if (!_args.Contains("--background"))
        {
            _tray.Show(T("app.running.title"), T("app.running.hint"), false);
        }
        HandleArgs(_args);
    }

    private TrayIcon CreateTray()
    {
        var tray = new TrayIcon(OpenSettings, () => _controller.OpenForClipboard(), () => ShelfWindow.Open([]), ShowHelp, SetPaused, _settings.Paused, () => Shutdown());
        return tray;
    }

    private void HandleArgs(string[] args)
    {
        if (args.Contains("--settings"))
            OpenSettings();
        var wheelIndex = Array.IndexOf(args, "--wheel");
        if (wheelIndex >= 0)
            _controller.QueuePaths(args.Skip(wheelIndex + 1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)));
    }

    private void OpenSettings() => SettingsWindow.ShowSingle(_settings, _tools, OnSettingsChanged);

    private void OnSettingsChanged()
    {
        if (LanguageManager.Preference != _settings.Language)
        {
            // Live language switch: no restart, no window is closed just because the language changed.
            LanguageManager.Apply(_settings.Language);
            LanguageManager.ApplyToOpenWindows();
            _controller.Relocalize();
            _tray?.Refresh();
            SettingsWindow.RelocalizeIfOpen();
            WheelEditorWindow.RelocalizeIfOpen();
            ShelfWindow.RelocalizeAll();
            JobsWindow.RelocalizeAll();
        }

        // R-20: turning gesture monitoring on/off takes effect immediately (no restart).
        var wantHooks = Policy.HooksAllowed(_settings, _args);
        if (wantHooks && !_input.IsRunning)
        {
            try
            {
                _input.Start();
                _tray?.Show("DropWheel", T("app.tray.gesturesOn"), false);
            }
            catch (InvalidOperationException ex)
            {
                AppLog.Error("could not start the gesture hooks", ex);
                Dialogs.Show(ex.Message, "DropWheel", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else if (!wantHooks && _input.IsRunning)
        {
            _input.Dispose();
            AppLog.Info("gesture hooks stopped by the setting/policy");
        }

        ApplyHotkey();
        if (_tray is not null)
            _tray.Paused = _settings.Paused;
    }

    /// <summary>
    /// Registers the chosen shortcut. If another app already owns it, switches to the first free preset
    /// and says so once, instead of leaving the user without a shortcut.
    /// </summary>
    private void ApplyHotkey()
    {
        var wanted = _settings.Hotkey;
        if (_hotkey is null || _activeHotkey == wanted)
            return;
        if (_hotkey.Register(wanted))
        {
            _activeHotkey = wanted;
            return;
        }

        var taken = Bidi.Value(AppSettings.HotkeyName(wanted));
        foreach (var preset in Enum.GetValues<HotkeyPreset>().Where(p => p != HotkeyPreset.None && p != wanted))
        {
            if (!_hotkey.Register(preset))
                continue;
            _activeHotkey = preset;
            _settings.Hotkey = preset;
            _settings.Save();
            SettingsWindow.RefreshIfOpen();
            var now = Bidi.Value(AppSettings.HotkeyName(preset));
            Notifications.Show(T("app.hotkey.changed.title"),
                T("app.hotkey.changed.body", taken, now));
            return;
        }

        _activeHotkey = HotkeyPreset.None;
        Notifications.Show(T("app.hotkey.taken.title"),
            T("app.hotkey.taken.body"), error: true);
    }

    private void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        _settings.Save();
    }

    private void ShowHelp()
    {
        var convert = Bidi.Value(AppSettings.KeyName(_settings.ConvertKey));
        var tools = Bidi.Value(AppSettings.KeyName(_settings.ToolsKey));
        var actions = Bidi.Value(AppSettings.KeyName(_settings.ActionsKey));
        var hotkey = Bidi.Value(AppSettings.HotkeyName(_settings.Hotkey));
        var text = T("help.body", convert, tools, actions, hotkey);
        Dialogs.Show(text + "\n\nDropWheel is based on FileAction by Metamorfolog (MIT).", T("help.title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        Notifications.Show(T("app.error.title"), Bidi.Value(e.Exception.Message), error: true);
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "log.txt"), $"[{DateTime.Now:s}] {ex}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _input?.Dispose();
        _tray?.Dispose();
        _settings.Save();
        base.OnExit(e);
    }

    private static void OpenLicenses()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "licenses");
            if (!System.IO.Directory.Exists(dir))
                dir = AppContext.BaseDirectory;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException)
        {
        }
    }
}
