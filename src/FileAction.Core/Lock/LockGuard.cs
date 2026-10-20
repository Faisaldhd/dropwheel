using System.IO;

namespace FileAction.Core.Lock;

/// <summary>Why a path cannot be protected. The UI turns this into one clear sentence.</summary>
public enum LockRefusal
{
    None,
    Missing,
    DriveRoot,
    SystemFolder,
    InstallFolder,
    DataFolder,
    AlreadyProtected,
    ReparsePoint,
    NotAFileOrFolder,
    NoAccess,
}

/// <summary>Where DropWheel lives and which folders belong to Windows, supplied so the rules stay pure.</summary>
public sealed record LockEnvironment(
    IReadOnlyList<string> SystemFolders,
    IReadOnlyList<string> ProtectedFolders,
    string? ProfileRoot = null,
    string? RestoreTempFolder = null)
{
    /// <summary>Where restore staging files live; never the user folder. Not part of the refusal rules.</summary>
    public string RestoreTempPath => string.IsNullOrWhiteSpace(RestoreTempFolder)
        ? Path.Combine(Path.GetTempPath(), "DropWheel", "restore")
        : RestoreTempFolder;

    /// <summary>The folders that are always refused: Windows, Program Files, ProgramData, the install and data folders.</summary>
    public static LockEnvironment Default()
    {
        var system = new List<string>();
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(system, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var protectedFolders = new List<string>();
        Add(protectedFolders, AppContext.BaseDirectory);
        Add(protectedFolders, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel"));
        return new LockEnvironment(system, protectedFolders,
            string.IsNullOrWhiteSpace(profile) ? null : LockPaths.Normalize(profile));
    }

    private static void Add(List<string> list, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;
        try
        {
            var normalized = LockPaths.Normalize(folder);
            if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                list.Add(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }
}

/// <summary>
/// The refusals that keep a lock from destroying something it should never touch: drive roots, Windows and
/// program folders, DropWheel's own install/data folders, links, and containers that are already protected.
/// </summary>
public static class LockGuard
{
    public static LockRefusal Check(string path, LockEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (string.IsNullOrWhiteSpace(path))
            return LockRefusal.Missing;

        string full;
        try
        {
            full = LockPaths.Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return LockRefusal.Missing;
        }

        if (LockFormat.IsProtectedName(full))
            return LockRefusal.AlreadyProtected;

        var isDirectory = Directory.Exists(full);
        var isFile = File.Exists(full);
        if (!isDirectory && !isFile)
            return LockRefusal.Missing;

        try
        {
            var attributes = File.GetAttributes(full);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return LockRefusal.ReparsePoint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LockRefusal.NoAccess;
        }

        if (LockPaths.IsDriveRoot(full))
            return LockRefusal.DriveRoot;

        foreach (var folder in environment.ProtectedFolders)
        {
            if (LockPaths.IsInside(full, folder))
                return LockRefusal.DataFolder;
        }

        foreach (var folder in environment.SystemFolders)
        {
            if (string.Equals(full, folder, StringComparison.OrdinalIgnoreCase) || LockPaths.IsInside(full, folder))
                return LockRefusal.SystemFolder;
        }

        // The profile root itself is refused, but protecting a file inside it is normal.
        if (environment.ProfileRoot is { } profileRoot &&
            string.Equals(full, profileRoot, StringComparison.OrdinalIgnoreCase))
        {
            return LockRefusal.SystemFolder;
        }

        return LockRefusal.None;
    }

    /// <summary>True when the item is one this build must never read as a plain source (already a container).</summary>
    public static bool IsAlreadyProtected(string path) => LockFormat.IsProtectedName(path);
}
