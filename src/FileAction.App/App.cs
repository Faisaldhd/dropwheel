using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileAction.App.Actions;
using FileAction.App.Diagnostics;
using FileAction.App.Input;
using FileAction.App.Jobs;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.App.Updates;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Updates;
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
        if (args.Contains("--set-watch"))
        {
            // Installer task "Watch mouse and keyboard": write the initial choice and exit immediately.
            // The setup uses this instead of shipping a pre-made settings.json, so an upgrade keeps the
            // user's existing file and the app stays the only writer of its settings.
            var settings = AppSettings.Load();
            settings.WatchGestures = true;
            settings.Save();
            return 0;
        }
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

        // 2.2.0: the update helper (--apply-update) must run before the single-instance check. The running
        // app starts it as a separate process and exits, so it can never be forwarded to the live instance.
        if (UpdateApplier.TryRun(args, out var updateExit))
            return updateExit;

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
    private UpdateService? _updates;
    private ThemeMode _theme;
    private bool _backgroundChecks;

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
        _theme = _settings.Theme;

        // 2.2.0: the update checker. It only reads the latest public GitHub release; downloading and
        // applying always need the user's click in the banner or the tray menu.
        _updates = new UpdateService(AppVersion(), UpdatePaths.IsPortable);
        SettingsWindow.Updates = _updates;
        _updates.UpdateAvailable += OnUpdateAvailable;
        _updates.StateChanged += OnUpdateStateChanged;
        AppLog.Info($"update service ready (version {AppVersion().ToString(3)}, " +
                    $"portable={_updates.IsPortable}, automatic={_settings.CheckForUpdates})");
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

        // A successful update restart passes --updated <version>; greet, then keep starting normally.
        AnnounceUpdatedIfRequested();
        if (_settings.CheckForUpdates)
            StartBackgroundChecks();
    }

    private TrayIcon CreateTray()
    {
        var tray = new TrayIcon(OpenSettings, () => _controller.OpenForClipboard(), () => ShelfWindow.Open([]), ShowHelp, SetPaused, _settings.Paused, () => Shutdown());
        tray.CheckForUpdates = () => _ = CheckUpdatesAsync();
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
            UpdateBanner.RelocalizeAll();
        }

        if (_theme != _settings.Theme)
        {
            // 2.2.0: the theme applies without a restart; the built controls hold brushes from the old
            // palette, so the settings window is rebuilt with the new one.
            _theme = _settings.Theme;
            Theme.Apply(_theme);
            AppLog.Info($"theme: {_theme}");
            SettingsWindow.RefreshIfOpen();
        }

        // 2.2.0: "Check for updates automatically" starts/stops the daily timer immediately. It only
        // checks; a download still needs a click.
        if (_settings.CheckForUpdates)
            StartBackgroundChecks();
        else
            StopBackgroundChecks();

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

    /// <summary>A background or manual check found a newer release: offer it in the tray (one click installs).</summary>
    private void OnUpdateAvailable(UpdateInfo info)
    {
        AppLog.Info($"update available: {info.Version}");
        void Offer()
        {
            _tray?.OfferUpdate(info.Version, () => _ = InstallUpdateAsync(info));
        }
        try
        {
            if (Dispatcher.CheckAccess())
                Offer();
            else
                Dispatcher.BeginInvoke((Action)Offer);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is shutting down; there is nobody left to offer the update to.
        }
    }

    /// <summary>Keeps the tray item in step with the checker: the item exists only while a newer release is known.</summary>
    private void OnUpdateStateChanged()
    {
        if (_updates?.Available is not null)
            return;
        try
        {
            if (Dispatcher.CheckAccess())
                _tray?.ClearUpdate();
            else
                Dispatcher.BeginInvoke((Action)(() => _tray?.ClearUpdate()));
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    /// <summary>Tray "Check for updates": one user-initiated check, reported with a balloon.</summary>
    private async Task CheckUpdatesAsync()
    {
        if (_updates is null || _updates.IsChecking || _updates.IsDownloading)
            return;
        AppLog.Info("update check requested from the tray");
        try
        {
            var status = await _updates.CheckAsync(true);
            if (status == UpdateCheckStatus.UpToDate)
            {
                _tray?.ClearUpdate();
                _tray?.Show("DropWheel",
                    T("update.check.upToDate", Bidi.Value(_updates.CurrentVersion.ToString(3))), false);
            }
            else if (status == UpdateCheckStatus.Failed)
            {
                var error = _updates.LastError;
                AppLog.Warn($"update check failed from the tray: {error ?? "no details"}");
                _tray?.Show(T("update.error.title"), T("update.check.failed", Bidi.Value(error ?? "")), true);
            }
            // Available: UpdateAvailable already showed the balloon and added the menu item.
        }
        catch (Exception ex)
        {
            AppLog.Error("update check from the tray failed", ex);
            _tray?.Show(T("update.error.title"), T("update.error.body", Bidi.Value(ex.Message)), true);
        }
    }

    /// <summary>Tray "Update to x.y.z": download, verify and apply; the app exits when the updater takes over.</summary>
    private async Task InstallUpdateAsync(UpdateInfo info)
    {
        if (_updates is null || _updates.IsDownloading)
            return;
        AppLog.Info($"installing update {info.Version} from the tray");
        try
        {
            var result = await UpdateApplier.DownloadAndApplyAsync(_updates, info, null, CancellationToken.None);
            if (result.Success)
                return; // the app exits for the update
            var error = string.IsNullOrWhiteSpace(result.Error) ? T("update.error.title") : result.Error;
            AppLog.Error($"update {info.Version} failed: {result.Error ?? "no details"}");
            _tray?.Show(T("update.error.title"), T("update.error.body", Bidi.Value(error)), true);
        }
        catch (Exception ex)
        {
            AppLog.Error("update download/apply failed", ex);
            _tray?.Show(T("update.error.title"), T("update.error.body", Bidi.Value(ex.Message)), true);
        }
    }

    /// <summary>Starts the 60 s / 24 h background checks; calling it twice is harmless.</summary>
    private void StartBackgroundChecks()
    {
        if (_updates is null || _backgroundChecks)
            return;
        _updates.StartBackgroundChecks();
        _backgroundChecks = true;
        AppLog.Info("automatic update checks started (first check in 60 s, then every 24 h)");
    }

    private void StopBackgroundChecks()
    {
        if (_updates is null || !_backgroundChecks)
            return;
        _updates.Stop();
        _backgroundChecks = false;
        AppLog.Info("automatic update checks stopped by the setting");
    }

    /// <summary>Handles <c>--updated &lt;version&gt;</c> / <c>--updated=&lt;version&gt;</c> left by the update helper.</summary>
    private void AnnounceUpdatedIfRequested()
    {
        const string prefix = "--updated=";
        string? version = null;
        var index = Array.IndexOf(_args, "--updated");
        if (index >= 0 && index + 1 < _args.Length && !_args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            version = _args[index + 1];
        }
        else
        {
            var prefixed = _args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
            if (prefixed is not null)
                version = prefixed[prefix.Length..];
        }
        if (string.IsNullOrWhiteSpace(version))
            return;
        AppLog.Info($"started after an update to {version}");
        UpdateApplier.AnnounceUpdated(version);
    }

    /// <summary>The running build's version as major.minor.build (the assembly revision stays out of the UI).</summary>
    private static Version AppVersion()
    {
        var version = typeof(App).Assembly.GetName().Version ?? new Version(2, 2, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
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
        _updates?.Dispose();
        _updates = null;
        SettingsWindow.Updates = null;
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
