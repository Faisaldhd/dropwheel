using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using FileAction.App.Diagnostics;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// One process-wide protection runtime: preferences, the DPAPI Rescuer vault, the lock service, the
/// session journal and the auto-lock manager. Created lazily, so simply having the tool available costs
/// nothing until it is used.
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
            Vault = new DpapiRescuerVault();
            Service = new LockService(LockEnvironment.Default(), new ProtectionHost());
            Journal = new LockJournalStore(ProtectionPaths.SessionsFolder);
            Sessions = new LockSessionManager(Service, Journal, Settings);
            Sessions.Locked += (title, text) => Notifications.Show(title, text);
        }
    }

    /// <summary>True when the Rescuer pair is held in the DPAPI vault rather than only in memory.</summary>
    public static bool RescuerPersists => Vault is DpapiRescuerVault { Persistent: true };

    /// <summary>The stored pair when both doors are usable; null means a prompt or first-time flow is due.</summary>
    public static RescuerSecrets? TryGetRescuerCredentials()
    {
        Initialize();
        var stored = Vault.Load();
        return stored is not null && stored.IsComplete ? stored : null;
    }

    /// <summary>
    /// Stores a Rescuer pair and marks the Rescuer as configured, but only after the vault reported that
    /// the sealed blob was durably written and read back. A failed save changes neither the disk nor the
    /// configured marker; the caller keeps its dialog open and warns instead of claiming success.
    /// </summary>
    public static bool SaveRescuer(RescuerSecrets secrets)
    {
        Initialize();
        if (!Vault.Save(secrets))
            return false;
        Settings.RescuerConfigured = true;
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The vault is the important copy; the marker can be repaired on the next start.
        }
        return true;
    }

    /// <summary>The random pair a first-ever Lock uses until Protection settings replaces it.</summary>
    public static RescuerSecrets NewProvisionalRescuer() => RescuerPolicy.CreateProvisional();

    /// <summary>
    /// Marks the current Rescuer key as used after it opens a protected file. Only the current key counts:
    /// an old key that still opens an older container must not unlock key rotation.
    /// </summary>
    public static void NoteRescuerKeyUsed(string typedSecret)
    {
        Initialize();
        var stored = Vault.Load();
        if (stored is null || stored.KeyUsed || !RescuerPolicy.MatchesKey(stored, typedSecret))
            return;
        Vault.Save(stored with { KeyUsed = true });
    }

    /// <summary>Opens the first-time Rescuer setup right after a successful first Lock.</summary>
    public static void ShowFirstRescuerSetup(IReadOnlyList<string> containers, RescuerSecrets provisional)
    {
        Initialize();
        if (containers.Count == 0 || provisional.Password is null)
            return;
        // Persist the paths before the dialog opens: a crash between saving the real pair and adopting
        // these files must leave evidence of the provisional-pair containers.
        RememberPendingAdoption(containers);
        LockSettingsWindow.ShowFirstTime(containers, provisional);
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
    /// The Rescuer pair for this run. A stored pair is returned silently; when the marker says a Rescuer
    /// exists but the DPAPI vault cannot be read, the "Rescuer credentials" dialog asks once and proves the
    /// pair against a real container before it is reused. Returns null when the user cancels or when no
    /// Rescuer has been created yet (the Protect view then uses a provisional pair).
    /// </summary>
    public static RescuerSecrets? RequireRescuer(Window? owner, string? nearbyPath = null)
    {
        Initialize();
        var stored = Vault.Load();
        if (stored is not null && stored.IsComplete)
            return stored;
        if (!Settings.RescuerConfigured)
            return null;

        // Re-entry: the prompt proves both slots against a real header before these values are baked into
        // new containers. When no container exists at all the user must explicitly acknowledge that this
        // is a new installation; a format-valid pair alone is never saved silently.
        var prompt = new RescuerSessionWindow(FindVerifierContainers(nearbyPath)) { Owner = owner };
        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
            return null;
        var keyUsed = stored?.KeyUsed == true && LockSecrets.SameSecret(stored.Key, prompt.RecoveryKey);
        var secrets = new RescuerSecrets(prompt.RecoveryKey, prompt.Password, keyUsed);
        if (!SaveRescuer(secrets))
        {
            Dialogs.Show(owner, T("tool.lock.settings.saveFailed"), T("tool.lock.rescuer.sessionTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return secrets;
    }

    /// <summary>
    /// Existing containers that can prove a re-entered Rescuer secret: the item's own folder first (the
    /// files a user protects usually live together), then the last container this install wrote, then any
    /// container named by an open crash-recovery journal. Only paths are returned; nothing is read here.
    /// </summary>
    public static IReadOnlyList<string> FindVerifierContainers(string? nearbyPath = null)
    {
        Initialize();
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                candidates.Add(LockPaths.Normalize(path));
        }

        if (!string.IsNullOrWhiteSpace(nearbyPath))
        {
            var directory = Directory.Exists(nearbyPath) ? nearbyPath : Path.GetDirectoryName(nearbyPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                                 .Where(LockFormat.IsProtectedName)
                                 .OrderByDescending(File.GetLastWriteTimeUtc))
                        Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        Add(Settings.LastContainerPath);
        foreach (var known in Settings.KnownContainerPaths)
            Add(known);
        try
        {
            foreach (var record in Journal.List())
                Add(record.ContainerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
    }

    /// <summary>Remembers the newest container so the next session can verify its Rescuer secrets.</summary>
    public static void RememberContainer(string containerPath) => RememberContainers([containerPath]);

    /// <summary>
    /// Remembers every container this install wrote. The list lets key rotation find the existing files
    /// that must be rewrapped before the vault advertises a new key; it is a path list, never a secret.
    /// </summary>
    public static void RememberContainers(IEnumerable<string> containerPaths)
    {
        ArgumentNullException.ThrowIfNull(containerPaths);
        var normalized = new List<string>();
        foreach (var path in containerPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            try
            {
                var full = LockPaths.Normalize(path);
                if (!normalized.Contains(full, StringComparer.OrdinalIgnoreCase))
                    normalized.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                           or PathTooLongException)
            {
            }
        }
        if (normalized.Count == 0)
            return;
        Initialize();
        Settings.LastContainerPath = normalized[^1];
        foreach (var path in normalized)
        {
            Settings.KnownContainerPaths.RemoveAll(existing =>
                string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
            Settings.KnownContainerPaths.Add(path);
        }
        var overflow = Settings.KnownContainerPaths.Count - ProtectionSettings.MaximumRememberedContainers;
        if (overflow > 0)
            Settings.KnownContainerPaths.RemoveRange(0, overflow);
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A path hint is a convenience; losing it only removes one verification candidate.
        }
    }

    /// <summary>Records the containers a first Lock wrote before the Rescuer setup dialog can run.</summary>
    public static void RememberPendingAdoption(IReadOnlyList<string> containerPaths)
    {
        ArgumentNullException.ThrowIfNull(containerPaths);
        Initialize();
        foreach (var path in containerPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            try
            {
                var full = LockPaths.Normalize(path);
                if (!Settings.PendingAdoptionContainers.Contains(full, StringComparer.OrdinalIgnoreCase))
                    Settings.PendingAdoptionContainers.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                           or PathTooLongException)
            {
            }
        }
        var overflow = Settings.PendingAdoptionContainers.Count - ProtectionSettings.MaximumRememberedContainers;
        if (overflow > 0)
            Settings.PendingAdoptionContainers.RemoveRange(0, overflow);
        SaveSettingsQuietly();
    }

    /// <summary>Clears adopted paths; with a list, removes only those entries.</summary>
    public static void ClearPendingAdoption(IEnumerable<string>? completed = null)
    {
        Initialize();
        if (completed is null)
        {
            Settings.PendingAdoptionContainers.Clear();
        }
        else
        {
            foreach (var path in completed)
            {
                Settings.PendingAdoptionContainers.RemoveAll(existing =>
                    string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
            }
        }
        SaveSettingsQuietly();
    }

    private static void SaveSettingsQuietly()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The containers key rotation must rewrap: every path this install remembers, the last container and
    /// the crash-recovery journal, restricted to files that still exist. Unknown files outside this set
    /// cannot be reached automatically; the Protection settings text says so.
    /// </summary>
    public static IReadOnlyList<string> KnownRescuerContainers()
    {
        Initialize();
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var full = LockPaths.Normalize(path);
                if (File.Exists(full) && !candidates.Contains(full, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                           or PathTooLongException)
            {
            }
        }

        foreach (var path in Settings.KnownContainerPaths)
            Add(path);
        Add(Settings.LastContainerPath);
        foreach (var path in FindVerifierContainers(Settings.LastContainerPath))
            Add(path);
        return candidates.Take(ProtectionSettings.MaximumRememberedContainers).ToList();
    }

    /// <summary>
    /// Rotates the Rescuer key for every known container, then commits the new pair to the vault. Any
    /// per-file failure rolls the already-rewrapped files back and keeps the old vault key; a vault save
    /// failure rolls every file back too. Returns null on success or a user-facing warning on failure.
    /// </summary>
    public static string? CommitNewRescuerKey(RescuerSecrets current, RescuerSecrets rotated)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(rotated);
        Initialize();
        if (string.IsNullOrEmpty(current.Password))
            return T("tool.lock.settings.rotationNeedsPassword");
        var containers = KnownRescuerContainers();
        var results = Service.RotateRescuerKey(containers, current.Password!, current.Key, rotated.Key);
        var failures = results.Count(result => result.Error is not null);
        if (failures > 0)
            return T("tool.lock.settings.rotationFailed", failures);
        if (SaveRescuer(rotated))
            return null;
        var rollback = Service.RotateRescuerKey(containers, current.Password!, rotated.Key, current.Key);
        var rollbackFailures = rollback.Count(result => result.Error is not null);
        // The failed save kept the new pair in the process-lifetime fallback; put the session back on the
        // key the files were rolled back to so this run cannot advertise an uncommitted key.
        Vault.Save(current);
        return rollbackFailures == 0
            ? T("tool.lock.settings.saveFailed")
            : T("tool.lock.settings.rotationRollbackFailed", rollbackFailures);
    }

    /// <summary>True once the runtime has created its journal/service; used by the exit hook.</summary>
    public static bool IsInitialized => _initialized;

    /// <summary>Re-encrypts every open session on a clean exit; failures keep their journal entries.</summary>
    public static void RelockOnExit()
    {
        if (!_initialized)
            return;
        try
        {
            Sessions.LockAllNow();
        }
        catch (Exception ex)
        {
            // The journal keeps the plaintext and the next startup offers recovery; never hide the app exit.
            AppLog.Error("lock exit relock", ex);
        }
    }
    /// <summary>Asks for the recovery key alone, for a key-only Rescuer password reset. Never stores the key.</summary>
    public static string? RequireRescuerKey(Window? owner)
    {
        Initialize();
        var stored = Vault.Load();
        if (stored is not null && !string.IsNullOrEmpty(stored.Key))
            return stored.Key;
        var window = new RescuerKeyWindow { Owner = owner };
        if (window.ShowDialog() != true || string.IsNullOrEmpty(window.RecoveryKey))
            return null;
        Vault.Save(new RescuerSecrets(window.RecoveryKey, stored?.Password, stored?.KeyUsed == true));
        return window.RecoveryKey;
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
