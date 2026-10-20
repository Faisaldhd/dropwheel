using System.IO;
using FileAction.Core.Lock;
using Microsoft.Win32;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The registry surface protection needs. It deliberately has no root parameter: every write goes to
/// <c>HKCU\Software\Classes</c>, so nothing can touch HKLM or another user.
/// </summary>
internal interface IProtectionRegistry
{
    bool KeyExists(string path);
    string? GetValue(string path, string? name);
    void SetValue(string path, string? name, string value);
    void DeleteTree(string path);
}

/// <summary>The real per-user registry implementation.</summary>
internal sealed class HkcuProtectionRegistry : IProtectionRegistry
{
    private const string Root = @"Software\Classes";

    public bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Full(path));
        return key is not null;
    }

    public string? GetValue(string path, string? name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Full(path));
        return key?.GetValue(name) as string;
    }

    public void SetValue(string path, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Full(path), writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteTree(string path)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(Full(path), throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A locked-down HKCU simply keeps the association; nothing else breaks.
        }
    }

    private static string Full(string path) => Root + "\\" + path.TrimStart('\\');
}

/// <summary>
/// Per-user file types for <c>.dwenc</c> and <c>.dwdir</c> with the purple multi-size icon and an
/// <c>open</c> verb. Registered only after the first protected file, and removable from Settings.
/// </summary>
internal static class ProtectionAssociations
{
    public const string FileProgId = "DropWheel.ProtectedFile";
    public const string FolderProgId = "DropWheel.ProtectedFolder";
    public const string FileExtension = ".dwenc";
    public const string FolderExtension = ".dwdir";

    /// <summary>The command-line switch the app must handle (see <see cref="LockSelfTest"/>).</summary>
    public const string OpenSwitch = "--lock-open";

    public static IReadOnlyList<string> Register(
        IProtectionRegistry registry, string exePath, string fileIconPath, string folderIconPath)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileIconPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderIconPath);

        var written = new List<string>();
        RegisterOne(registry, FileExtension, FileProgId, "DropWheel protected file", exePath, fileIconPath, written);
        RegisterOne(registry, FolderExtension, FolderProgId, "DropWheel protected folder", exePath, folderIconPath,
            written);
        return written;
    }

    private static void RegisterOne(
        IProtectionRegistry registry, string extension, string progId, string description,
        string exePath, string iconPath, List<string> written)
    {
        registry.SetValue(extension, null, progId);
        written.Add(extension);

        registry.SetValue(progId, null, description);
        registry.SetValue(progId, "FriendlyTypeName", description);
        registry.SetValue(progId + @"\DefaultIcon", null, iconPath + ",0");
        registry.SetValue(progId + @"\shell\open", null, "Open");
        registry.SetValue(progId + @"\shell\open\command", null,
            $"\"{exePath}\" {OpenSwitch} \"%1\"");
        written.Add(progId);
    }

    public static void Remove(IProtectionRegistry registry)
    {
        registry.DeleteTree(FileExtension);
        registry.DeleteTree(FolderExtension);
        registry.DeleteTree(FileProgId);
        registry.DeleteTree(FolderProgId);
    }

    public static bool IsRegistered(IProtectionRegistry registry) =>
        registry.KeyExists(FileExtension) && registry.KeyExists(FolderExtension);

    /// <summary>Copies the generated icons where the registry can point at them for every user session.</summary>
    public static (string FileIcon, string FolderIcon) EnsureIcons()
    {
        var folder = ProtectionPaths.IconsFolder;
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "protected-file.ico");
        var directory = Path.Combine(folder, "protected-folder.ico");
        LockIcons.WriteFileIcon(file);
        LockIcons.WriteFolderIcon(directory);
        return (file, directory);
    }
}
