using System.Text;

namespace FileAction.Core.Lock;

/// <summary>Naming rules for protected containers and their restored originals.</summary>
public static class LockPaths
{
    /// <summary>The restored-on-collision suffix; the UI shows it verbatim.</summary>
    public const string RestoredSuffix = " (restored)";

    /// <summary>
    /// <c>report.pdf</c> → <c>report.pdf.dwenc</c>, <c>Photos</c> → <c>Photos.dwdir</c>. The original name
    /// stays visible so a folder full of protected files is understandable without opening anything.
    /// </summary>
    public static string TargetName(string sourcePath, bool isDirectory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("The path has no file or folder name.", nameof(sourcePath));
        return name + (isDirectory ? LockFormat.FolderExtension : LockFormat.FileExtension);
    }

    public static string TargetPath(string sourcePath, bool isDirectory, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        return Path.Combine(outputDirectory, TargetName(sourcePath, isDirectory));
    }

    /// <summary>Work file written next to the final name (<c>name.dwenc.tmp</c>).</summary>
    public static string TempPath(string targetPath) => targetPath + LockFormat.TempSuffix;

    /// <summary>A staging path used while restoring a folder, next to where it will land.</summary>
    public static string RestoreStagingPath(string destinationDirectory, string name) =>
        Path.Combine(destinationDirectory, "." + name + ".dwrestore" + LockFormat.TempSuffix);

    /// <summary>The original name with a suffix before its extension: <c>report (restored).pdf</c>.</summary>
    public static string WithRestoredSuffix(string originalName)
    {
        var extension = Path.GetExtension(originalName);
        var stem = extension.Length > 0 ? originalName[..^extension.Length] : originalName;
        return stem + RestoredSuffix + extension;
    }

    /// <summary>First free name in <paramref name="directory"/>; numbering only after the first suffix.</summary>
    public static string UniqueRestoredPath(string directory, string originalName, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);
        var first = Path.Combine(directory, WithRestoredSuffix(originalName));
        if (!exists(first))
            return first;
        var extension = Path.GetExtension(originalName);
        var stem = extension.Length > 0 ? originalName[..^extension.Length] : originalName;
        for (var n = 2; n < 10_000; n++)
        {
            var candidate = Path.Combine(directory, $"{stem}{RestoredSuffix} {n}{extension}");
            if (!exists(candidate))
                return candidate;
        }
        throw new IOException("Every restored name is taken in this folder.");
    }

    /// <summary>The neutral name shown in the result list and the unlock panel.</summary>
    public static string DisplayName(string protectedPath)
    {
        var name = Path.GetFileName(protectedPath);
        foreach (var extension in new[] { LockFormat.FileExtension, LockFormat.FolderExtension })
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return name[..^extension.Length];
        }
        return name;
    }

    /// <summary>Full path without a trailing separator; the drive root keeps its backslash.</summary>
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>True when <paramref name="path"/> is the folder itself or lies below it.</summary>
    public static bool IsInside(string path, string folder)
    {
        var normalizedPath = Normalize(path);
        var normalizedFolder = Normalize(folder);
        if (string.Equals(normalizedPath, normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = Path.EndsInDirectorySeparator(normalizedFolder) ? normalizedFolder : normalizedFolder + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDriveRoot(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return !string.IsNullOrEmpty(root) &&
               string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
                   Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A UTF-8 relative archive path with forward slashes, never rooted and never with "..".</summary>
    public static bool IsSafeRelativePath(string? relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 1024)
            return false;
        if (Path.IsPathRooted(relative) || relative[0] is '/' or '\\')
            return false;
        if (relative.Contains(':'))
            return false;
        if (relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;
        foreach (var segment in relative.Split('/'))
        {
            if (segment.Length is 0 or > 255)
                return false;
            if (segment is "." or "..")
                return false;
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
        }
        return true;
    }

    /// <summary>Converts a relative archive path to a rooted path under <paramref name="root"/>.</summary>
    public static string CombineSafe(string root, string relative)
    {
        if (!IsSafeRelativePath(relative))
            throw new LockDamagedException("archive path");
        var parts = relative.Split('/');
        var path = root;
        foreach (var part in parts)
            path = Path.Combine(path, part);
        if (!IsInside(path, root))
            throw new LockDamagedException("archive escape");
        return path;
    }

    internal static string ToArchivePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Replace('\\', '/');
    }
}
