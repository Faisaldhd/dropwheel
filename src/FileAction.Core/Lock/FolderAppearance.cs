using System.IO;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>
/// The folder badge: a <c>desktop.ini</c> that points Explorer at DropWheel's purple-locked folder icon,
/// with the System/ReadOnly attributes Explorer requires. The bytes that were there before are kept in the
/// encrypted metadata, so unlocking restores them exactly (or removes the file when there was none).
/// </summary>
public static class FolderAppearance
{
    public const string DesktopIniName = "desktop.ini";

    public static string DesktopIniPath(string folder) => Path.Combine(folder, DesktopIniName);

    /// <summary>The existing <c>desktop.ini</c> bytes, or null when the folder has none.</summary>
    public static byte[]? ReadPrevious(string folder)
    {
        var path = DesktopIniPath(folder);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The attributes the previous desktop.ini carried; 0 when there was none.</summary>
    public static int ReadPreviousAttributes(string folder)
    {
        var path = DesktopIniPath(folder);
        try
        {
            return File.Exists(path) ? (int)File.GetAttributes(path) & 0xFFFF : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>Writes the badge file and marks the folder the way Explorer expects.</summary>
    public static void Apply(string folder, string iconPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconPath);
        var ini = "[.ShellClassInfo]\r\nIconResource=" + iconPath + ",0\r\n";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(ini)).ToArray();
        var path = DesktopIniPath(folder);
        // A Hidden/System file cannot be opened for writing unless its attributes are cleared first.
        if (File.Exists(path))
            File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, bytes);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        ApplyFolderAttributes(folder);
    }

    /// <summary>Sets ReadOnly + System on the folder itself, the combination Explorer shows as customised.</summary>
    public static void ApplyFolderAttributes(string folder)
    {
        var attributes = File.GetAttributes(folder);
        if ((attributes & FileAttributes.Directory) == 0)
            attributes |= FileAttributes.Directory;
        File.SetAttributes(folder, attributes | FileAttributes.ReadOnly | FileAttributes.System);
    }

    /// <summary>
    /// Restores the previous bytes byte-for-byte, or removes the badge file when the folder had none. The
    /// previous attributes are restored only when the file existed before.
    /// </summary>
    public static void Restore(string folder, byte[]? previousBytes, int previousAttributes)
    {
        var path = DesktopIniPath(folder);
        try
        {
            if (previousBytes is null)
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                return;
            }

            if (File.Exists(path))
                File.SetAttributes(path, FileAttributes.Normal);
            File.WriteAllBytes(path, previousBytes);
            var attributes = (FileAttributes)(previousAttributes & 0xFFFF);
            if (attributes != 0)
                File.SetAttributes(path, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Restoring the icon is cosmetic; the decrypted content is already verified.
        }
    }

    /// <summary>Restores the folder attributes captured before protection (ReadOnly last).</summary>
    public static void RestoreFolderAttributes(string folder, int attributes)
    {
        try
        {
            var restore = (FileAttributes)(attributes & 0xFFFF);
            if ((restore & FileAttributes.Directory) == 0)
                restore |= FileAttributes.Directory;
            File.SetAttributes(folder, restore);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Applies the stored timestamps to a restored file or folder.</summary>
    public static void RestoreTimes(string path, LockMeta meta, bool directory)
    {
        try
        {
            var created = new DateTime(meta.CreationTimeUtcTicks, DateTimeKind.Utc);
            var modified = new DateTime(meta.LastWriteTimeUtcTicks, DateTimeKind.Utc);
            if (created.Year > 1601)
            {
                if (directory)
                    Directory.SetCreationTimeUtc(path, created);
                else
                    File.SetCreationTimeUtc(path, created);
            }
            if (modified.Year > 1601)
            {
                if (directory)
                    Directory.SetLastWriteTimeUtc(path, modified);
                else
                    File.SetLastWriteTimeUtc(path, modified);
            }
            var accessed = new DateTime(meta.LastAccessTimeUtcTicks, DateTimeKind.Utc);
            if (accessed.Year > 1601)
            {
                if (directory)
                    Directory.SetLastAccessTimeUtc(path, accessed);
                else
                    File.SetLastAccessTimeUtc(path, accessed);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // Timestamps are best effort; content is what must be exact.
        }
    }
}
