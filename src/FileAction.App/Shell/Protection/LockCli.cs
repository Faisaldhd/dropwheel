using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.App.UI;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The window a protected file opens when it is double-clicked (through the per-user file association).
/// It asks for any of the three secrets and then opens the auto-lock session.
/// </summary>
internal sealed class LockOpenWindow : Window
{
    private readonly string _path;
    private readonly SecretField _secret;
    private readonly TextBlock _error;
    private int _failures;

    public LockOpenWindow(string path)
    {
        _path = path;
        LockRuntime.Initialize();
        var body = LockUi.Configure(this, T("tool.lock.title"), 440, 330);
        body.Children.Add(new TextBlock
        {
            Text = Bidi.Value(LockPaths.DisplayName(path)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        });
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.unlock.secret"),
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 8, 0, 5),
        });
        _secret = new SecretField(T("tool.lock.unlock.secret"));
        body.Children.Add(_secret);
        _error = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.BadgeErrorText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        body.Children.Add(_error);
        body.Children.Add(new TextBlock
        {
            Text = T("tool.lock.unlock.ladder"),
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        });

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var remove = Controls.Button(T("tool.lock.unlock.remove"), RemoveProtection);
        var open = Controls.Button(T("tool.lock.unlock.open"), Open, primary: true);
        DockPanel.SetDock(remove, Dock.Left);
        DockPanel.SetDock(open, Dock.Right);
        buttons.Children.Add(remove);
        buttons.Children.Add(open);
        body.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Open();
                e.Handled = true;
            }
        };
        Closing += (_, _) => _secret.HideReveal();
        Loaded += (_, _) => _secret.FocusInput();
    }

    public static void ShowFor(string path)
    {
        var window = new LockOpenWindow(path);
        window.Show();
        window.Activate();
    }

    private async void Open()
    {
        if (string.IsNullOrEmpty(_secret.Text))
        {
            _error.Text = T("tool.lock.unlock.empty");
            return;
        }
        try
        {
            await LockRuntime.Sessions.OpenAsync(_path, _secret.Text);
            Close();
        }
        catch (LockWrongSecretException)
        {
            _failures++;
            var delay = LockSecrets.DelayAfter(_failures);
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
            _error.Text = LockRuntime.Describe(new LockWrongSecretException());
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                       or LockPolicyException or ArgumentException)
        {
            _error.Text = LockRuntime.Describe(ex);
        }
    }

    private async void RemoveProtection()
    {
        if (Dialogs.Show(this, T("tool.lock.removeConfirm.body", Bidi.Value(LockPaths.DisplayName(_path))),
                T("tool.lock.removeConfirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            // If the item is open in a session, relock and close that session first; otherwise
            // Unprotect refuses with "Lock this file first" after a just-opened file.
            if (!await LockRuntime.Sessions.CloseForRemovalAsync(_path))
            {
                _error.Text = T("tool.lock.error.openSession");
                return;
            }
            var outcome = LockRuntime.Service.Unprotect(_path, _secret.Text,
                LockRuntime.Settings.RecycleContainerAfterUnlock);
            LockIconCache.RemoveItem(outcome.RestoredPath);
            var text = T("tool.lock.removeDone", Bidi.Value(outcome.RestoredPath));
            if (!outcome.ContainerRemoved)
                text += Environment.NewLine + T("tool.lock.removeLeftover", Bidi.Value(outcome.ContainerError ?? ""));
            Dialogs.Show(this, text, T("tool.lock.title"), MessageBoxButton.OK, MessageBoxImage.Information);
            OpenRestored(outcome.RestoredPath);
            Close();
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                       or LockPolicyException or ArgumentException)
        {
            _error.Text = LockRuntime.Describe(ex);
        }
    }

    private static void OpenRestored(string path)
    {
        try
        {
            // Never execute a restored file: a .exe/.bat/.ps1/.lnk could run. Reveal it in Explorer,
            // selected, and let the user decide what to open.
            var isFolder = Directory.Exists(path);
            var folder = isFolder ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder))
                return;
            var arguments = isFolder ? $"\"{folder}\"" : $"/select,\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // The file is already restored; failing to reveal it is not a lock failure.
        }
    }
}

/// <summary>
/// The command-line hook the per-user file association and the supervisor use. Call
/// <see cref="TryHandle"/> from the app's argument handler and <see cref="RunStartupRecovery"/> once the
/// main window exists; nothing else in the app has to know about lock files.
/// </summary>
internal static class LockCli
{
    public static bool TryHandle(string[] args)
    {
        var index = Array.IndexOf(args, ProtectionAssociations.OpenSwitch);
        if (index < 0 || index + 1 >= args.Length)
            return false;
        var path = args[index + 1];
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Dialogs.Show(T("tool.lock.error.missing"), T("tool.lock.title"), MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return true;
        }
        if (!LockFormat.IsProtectedName(path))
        {
            Dialogs.Show(T("tool.lock.error.notProtected"), T("tool.lock.title"), MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return true;
        }
        LockRuntime.Initialize();
        LockOpenWindow.ShowFor(path);
        return true;
    }

    /// <summary>Scans the session journal and offers to re-encrypt anything a crash left open.</summary>
    public static void RunStartupRecovery()
    {
        LockRuntime.Initialize();
        LockRuntime.Sessions.RecoverOnStartup(null);
    }
}

/// <summary>
/// The lock/unlock round trip for <c>--self-test</c>: a sample file and a small folder are protected,
/// unlocked and compared by SHA-256, and the folder's previous desktop.ini must return byte for byte.
/// The supervisor only has to call <see cref="Run"/> from the existing self-test steps.
/// </summary>
internal static class LockSelfTest
{
    public static void Run(string workRoot, List<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);
        ArgumentNullException.ThrowIfNull(log);
        var root = Path.Combine(workRoot, "lock-self-test");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        var credentials = new LockCredentials
        {
            FilePassword = "self-test-file-password",
            RescuerPassword = "self-test-rescuer-password",
            RescuerKey = RescuerKey.Generate(),
        };
        var service = new LockService(LockEnvironment.Default(), NullLockHost.Instance);

        // ---- one file
        var file = Path.Combine(root, "sample.bin");
        var payload = new byte[128 * 1024];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(file, payload);
        var before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
        var protectedFile = service.Protect(file, root, credentials).OutputPath;
        if (File.Exists(file))
            throw new InvalidOperationException("self-test: the plaintext file was left behind");
        if (!File.Exists(protectedFile))
            throw new InvalidOperationException("self-test: the protected file was not created");
        var verified = service.Verify(protectedFile, credentials.FilePassword);
        if (Convert.ToHexString(verified.Digest.Sha256) != before)
            throw new InvalidOperationException("self-test: the protected file does not verify");
        var restored = service.Unprotect(protectedFile, credentials.FilePassword, recycleContainer: false);
        if (!File.Exists(restored.RestoredPath))
            throw new InvalidOperationException("self-test: the file was not restored");
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(restored.RestoredPath))) != before)
            throw new InvalidOperationException("self-test: the restored file differs");
        if (File.Exists(protectedFile))
            throw new InvalidOperationException("self-test: the protected file was left behind");
        log.Add("ok lock/file (protect, verify, unlock, SHA-256 equal)");

        // ---- one folder with a previous desktop.ini
        var folder = Path.Combine(root, "sample-folder");
        Directory.CreateDirectory(Path.Combine(folder, "nested"));
        File.WriteAllText(Path.Combine(folder, "one.txt"), "one");
        File.WriteAllText(Path.Combine(folder, "nested", "two.txt"), "two");
        var previousIni = System.Text.Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nInfoTip=keep me\r\n");
        File.WriteAllBytes(Path.Combine(folder, "desktop.ini"), previousIni);
        var protectedFolder = service.Protect(folder, root, credentials,
            LockRuntime.Icons.FolderIcon).OutputPath;
        if (Directory.Exists(folder))
            throw new InvalidOperationException("self-test: the folder was left behind");
        var folderResult = service.Unprotect(protectedFolder, credentials.RescuerPassword, recycleContainer: false);
        if (!Directory.Exists(folderResult.RestoredPath))
            throw new InvalidOperationException("self-test: the folder was not restored");
        if (File.ReadAllText(Path.Combine(folderResult.RestoredPath, "nested", "two.txt")) != "two")
            throw new InvalidOperationException("self-test: folder content differs");
        var ini = File.ReadAllBytes(Path.Combine(folderResult.RestoredPath, "desktop.ini"));
        if (!ini.SequenceEqual(previousIni))
            throw new InvalidOperationException("self-test: desktop.ini was not restored byte for byte");
        log.Add("ok lock/folder (archive, nested file, desktop.ini restored)");

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Add("warn lock/self-test cleanup: " + ex.Message);
        }
    }
}
