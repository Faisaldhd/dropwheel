using System.IO;
using System.Security.Cryptography;
using System.Text;
using FileAction.Core.Lock;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// Composed per-item and per-type icons. The shell icon is the expensive part, so a type icon is built
/// once per original extension and reused; each item then gets a cheap copy under <c>items</c>. The
/// cache is under <c>%LOCALAPPDATA%\DropWheel\icons</c> and is removed on unlock/uninstall.
/// </summary>
internal static class LockIconCache
{
    public static string? RootOverride { get; set; }

    public static string Root => RootOverride ?? Path.Combine(ProtectionPaths.DataFolder, "icons");

    private static string ItemsFolder => Path.Combine(Root, "items");

    private static string TypesFolder => Path.Combine(Root, "types");

    private static string AssociationFolder => Path.Combine(Root, "assoc");

    /// <summary>One composed ICO per original extension (or "folder"), reused for every item of that type.</summary>
    public static string EnsureTypeIcon(string originalPath, bool folder)
    {
        var key = folder ? "folder" : ExtensionKey(originalPath);
        var path = Path.Combine(TypesFolder, key + ".ico");
        if (File.Exists(path))
            return path;
        WriteAtomic(path, LockIcons.ComposeIco(originalPath, folder));
        return path;
    }

    /// <summary>A per-item ICO copied from the type cache; used by the tool and folder desktop.ini.</summary>
    public static string EnsureItemIcon(string originalPath, bool folder)
    {
        var type = EnsureTypeIcon(originalPath, folder);
        var path = ItemPath(originalPath);
        if (!File.Exists(path))
            WriteAtomic(path, File.ReadAllBytes(type));
        return path;
    }

    public static string ItemPath(string originalPath)
    {
        var full = Path.GetFullPath(originalPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))
            .ToLowerInvariant()[..20];
        return Path.Combine(ItemsFolder, hash + ".ico");
    }

    public static void RemoveItem(string originalPath)
    {
        try
        {
            var path = ItemPath(originalPath);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Association icons: the real system generic-file/folder icon plus the padlock.</summary>
    public static (string FileIcon, string FolderIcon) EnsureAssociationIcons()
    {
        var file = Path.Combine(AssociationFolder, "protected-file.ico");
        var folder = Path.Combine(AssociationFolder, "protected-folder.ico");
        if (!File.Exists(file))
        {
            var temp = Path.Combine(Path.GetTempPath(), "dw-assoc-" + Guid.NewGuid().ToString("N") + ".dat");
            File.WriteAllBytes(temp, []);
            try
            {
                WriteAtomic(file, LockIcons.ComposeIco(temp, folder: false));
            }
            finally
            {
                File.Delete(temp);
            }
        }
        if (!File.Exists(folder))
        {
            var temp = Path.Combine(Path.GetTempPath(), "dw-assoc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                WriteAtomic(folder, LockIcons.ComposeIco(temp, folder: true));
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }
        return (file, folder);
    }

    /// <summary>Deletes every composed icon (uninstall, and tests).</summary>
    public static void Clear()
    {
        foreach (var directory in new[] { ItemsFolder, TypesFolder, AssociationFolder })
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        try
        {
            var legacyFile = Path.Combine(Root, "protected-file.ico");
            var legacyFolder = Path.Combine(Root, "protected-folder.ico");
            if (File.Exists(legacyFile))
                File.Delete(legacyFile);
            if (File.Exists(legacyFolder))
                File.Delete(legacyFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ExtensionKey(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
            return "file";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            extension = extension.Replace(invalid, '_');
        return extension;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }
}
