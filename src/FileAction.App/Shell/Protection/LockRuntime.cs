using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// One process-wide protection runtime: preferences, the in-memory Rescuer secrets (never written to
/// disk), the lock service, the session journal and the auto-lock manager. Created lazily, so simply having
/// the tool available costs nothing until it is used.
/// </summary>
internal static class LockRuntime
{
    private static readonly object Gate = new();
    private static bool _initialized;
    private static (string FileIcon, string FolderIcon)? _icons;

    public static ProtectionSettings Settings { get; private set; } = new();
    public static IRescuerVault Vault { get; private set; } = new InMemoryRescuerVault();
    public static LockService Service { get; private set; } = null!;
    public static LockJournalStore Journal { get; private set; } = null!;
    public static LockSessionManager Sessions { get; private set; } = null!;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
                return;
            _initialized = true;
            Settings = ProtectionSettings.Load();
            Vault = new InMemoryRescuerVault();
            // A previous build could have written a DPAPI vault with the Rescuer password and key. Remove
            // it on first use: the secrets now live only in memory for the session.
            try
            {
                var legacy = Path.Combine(ProtectionPaths.DataFolder, "protection", "rescuer.dat");
                if (File.Exists(legacy))
                    File.Delete(legacy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            Service = new LockService(LockEnvironment.Default(), new ProtectionHost());
            Journal = new LockJournalStore(ProtectionPaths.SessionsFolder);
            Sessions = new LockSessionManager(Service, Journal, Settings);
            Sessions.Locked += (title, text) => Notifications.Show(title, text);
        }
    }

    /// <summary>The generated icon paths, written once next to the protection data.</summary>
    public static (string FileIcon, string FolderIcon) Icons
    {
        get
        {
            lock (Gate)
            {
                _icons ??= ProtectionAssociations.EnsureIcons();
                return _icons.Value;
            }
        }
    }

    /// <summary>
    /// The Rescuer secrets for this session. They live only in memory: the first-ever setup creates the
    /// password and the one-time key; every later session asks for both once, because neither is written to
    /// the settings file, the journal or any other file. Returns null when the user cancels.
    /// </summary>
    public static RescuerSecrets? RequireRescuer(Window? owner)
    {
        Initialize();
        var stored = Vault.Load();
        if (stored is not null && !string.IsNullOrEmpty(stored.Password))
            return stored;

        if (!Settings.RescuerConfigured)
        {
            var setup = new RescuerSetupWindow(RescuerSetupMode.FirstTime) { Owner = owner };
            if (setup.ShowDialog() != true || setup.Result is null)
                return null;
            Vault.Save(setup.Result);
            Settings.RescuerConfigured = true;
            Settings.Save();
            return setup.Result;
        }

        var prompt = new RescuerSessionWindow { Owner = owner };
        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
            return null;
        var secrets = new RescuerSecrets(prompt.RecoveryKey, prompt.Password);
        Vault.Save(secrets);
        return secrets;
    }

    /// <summary>Registers the per-user file types once, after the first successful protection.</summary>
    public static void EnsureAssociation()
    {
        Initialize();
        if (Settings.AssociationsRegistered)
        {
            // Refresh the icon files even when the associations already exist: an app update may ship
            // new artwork (the purple badge replaced the earlier pink one).
            try
            {
                _ = Icons;
                new ProtectionHost().NotifyShell(string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            return;
        }
        try
        {
            var icons = Icons;
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return;
            ProtectionAssociations.Register(new HkcuProtectionRegistry(), exe, icons.FileIcon, icons.FolderIcon);
            Settings.AssociationsRegistered = true;
            Settings.Save();
            new ProtectionHost().NotifyShell(string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
            // Association is a convenience; a locked-down profile simply keeps generic icons.
        }
    }

    public static void RemoveAssociation()
    {
        Initialize();
        ProtectionAssociations.Remove(new HkcuProtectionRegistry());
        LockIconCache.Clear();
        Settings.AssociationsRegistered = false;
        Settings.Save();
        new ProtectionHost().NotifyShell(string.Empty);
    }

    /// <summary>Text for a refusal, with the offending path already isolated for RTL.</summary>
    public static string Describe(LockRefusal refusal, string path) => refusal switch
    {
        LockRefusal.Missing => T("tool.lock.error.missing"),
        LockRefusal.DriveRoot => T("tool.lock.error.driveRoot"),
        LockRefusal.SystemFolder => T("tool.lock.error.systemFolder"),
        LockRefusal.InstallFolder => T("tool.lock.error.installFolder"),
        LockRefusal.DataFolder => T("tool.lock.error.dataFolder"),
        LockRefusal.AlreadyProtected => T("tool.lock.error.alreadyProtected"),
        LockRefusal.ReparsePoint => T("tool.lock.error.reparse"),
        LockRefusal.NotAFileOrFolder => T("tool.lock.error.missing"),
        _ => T("tool.lock.error.generic", Bidi.Value(Path.GetFileName(path))),
    };

    /// <summary>A generic, non-revealing message for any lock exception.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        LockWrongSecretException => T("tool.lock.error.wrongSecret"),
        LockTruncatedException => T("tool.lock.error.truncated"),
        LockNotProtectedException => T("tool.lock.error.notProtected"),
        LockUnsupportedException => T("tool.lock.error.unsupported"),
        LockDamagedException => T("tool.lock.error.damaged"),
        LockRefusedException refused => Describe(refused.Reason, refused.Path),
        LockTargetExistsException => T("tool.lock.error.targetExists"),
        LockFileInUseException inUse => T("tool.lock.error.fileInUse", Bidi.Value(inUse.RelativePath)),
        LockPolicyException policy => policy.Message,
        _ => T("tool.lock.error.generic", Bidi.Value(ex.Message)),
    };
}

/// <summary>True when a program still holds the file (or any file inside a folder).</summary>
internal interface ILockHoldProbe
{
    bool HoldsFile(string path);
    bool AnyFileHeld(string folder);
}

/// <summary>Opens the temporary plaintext with the user's default program.</summary>
internal interface ILockLauncher
{
    void Open(string path, bool isDirectory);
}

internal sealed class WindowsHoldProbe : ILockHoldProbe
{
    public bool HoldsFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public bool AnyFileHeld(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return false;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (HoldsFile(file))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }
}

/// <summary>
/// Asks Explorer (through Shell.Application) whether any window or tab currently shows the folder. When
/// the shell cannot answer, the answer is "no" and the idle fallback in <see cref="AutoLockRules"/>
/// decides instead.
/// </summary>
internal interface IExplorerProbe
{
    bool ShowsFolder(string folder);
}

internal sealed class ShellExplorerProbe : IExplorerProbe
{
    public bool ShowsFolder(string folder)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return false;
            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;
            var windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null)
                as System.Collections.IEnumerable;
            if (windows is null)
                return false;
            foreach (var window in windows)
            {
                if (window is null)
                    continue;
                var location = ReadLocation(window);
                if (!string.IsNullOrEmpty(location) && LockPaths.IsInside(folder, location))
                    return true;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TargetInvocationException or InvalidCastException
                                       or NotSupportedException or ArgumentException or MemberAccessException)
        {
            return false;
        }
        return false;
    }

    private static string? ReadLocation(object window)
    {
        var type = window.GetType();
        var url = type.InvokeMember("LocationURL", BindingFlags.GetProperty, null, window, null) as string;
        if (!string.IsNullOrEmpty(url) && url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return LockPaths.Normalize(new Uri(url).LocalPath);
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidOperationException or ArgumentException)
            {
            }
        }
        try
        {
            var document = type.InvokeMember("Document", BindingFlags.GetProperty, null, window, null);
            var shellFolder = document?.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, document, null);
            var self = shellFolder?.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, shellFolder, null);
            var path = self?.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null) as string;
            return string.IsNullOrEmpty(path) ? null : LockPaths.Normalize(path);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or ArgumentException)
        {
            return null;
        }
    }
}

internal sealed class ShellLockLauncher : ILockLauncher
{
    public void Open(string path, bool isDirectory)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = true };
        if (isDirectory)
        {
            start = new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true };
        }
        Process.Start(start);
    }
}
