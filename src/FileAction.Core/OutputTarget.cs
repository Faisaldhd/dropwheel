namespace FileAction.Core;

/// <summary>Why the folder detected under the cursor was, or was not, used for one drop.</summary>
public enum OutputTargetStatus
{
    /// <summary>The detected folder exists, differs from the source folder and is writable.</summary>
    UseDetectedFolder,

    /// <summary>Nothing was detected (the wheel was opened away from an Explorer window).</summary>
    NoFolderDetected,

    /// <summary>The detected folder is the source folder, so "next to the file" is already right.</summary>
    SameAsSource,

    /// <summary>The folder is gone or is not a real file-system folder (deleted, unmounted, virtual).</summary>
    Missing,

    /// <summary>The folder exists but a file cannot be created in it (read-only, protected, no access).</summary>
    NotWritable,
}

/// <summary>The folder a drop should write into, plus the reason when it must fall back.</summary>
public readonly record struct OutputTargetResult(string? Directory, OutputTargetStatus Status)
{
    /// <summary>True when <see cref="Directory"/> should be used for this operation.</summary>
    public bool UsesDetectedFolder => Status == OutputTargetStatus.UseDetectedFolder;
}

/// <summary>
/// Resolves the folder that a drag-and-drop operation writes its results into: the folder under the
/// cursor when the wheel opened, provided it still exists, can be written to and is not the folder the
/// dragged file already lives in. Everything here is pure; the file-system probes are supplied by the
/// caller so the rules can be unit tested without a UI, a shell window or a real disk.
/// </summary>
public static class OutputTarget
{
    /// <param name="sourcePath">The dragged file or folder; its own folder is the fallback.</param>
    /// <param name="detectedFolder">Folder found under the cursor, or null when none was found.</param>
    /// <param name="directoryExists">Probe: does this folder still exist?</param>
    /// <param name="canWrite">Probe: can a file be created in this folder right now?</param>
    public static OutputTargetResult Resolve(
        string sourcePath,
        string? detectedFolder,
        Func<string, bool> directoryExists,
        Func<string, bool> canWrite)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        ArgumentNullException.ThrowIfNull(canWrite);

        if (string.IsNullOrWhiteSpace(detectedFolder))
            return new OutputTargetResult(null, OutputTargetStatus.NoFolderDetected);

        if (!TryNormalize(detectedFolder, out var target))
            return new OutputTargetResult(null, OutputTargetStatus.Missing);

        if (!directoryExists(target))
            return new OutputTargetResult(null, OutputTargetStatus.Missing);

        if (TryNormalize(SourceFolder(sourcePath), out var source) &&
            string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
        {
            return new OutputTargetResult(null, OutputTargetStatus.SameAsSource);
        }

        return canWrite(target)
            ? new OutputTargetResult(target, OutputTargetStatus.UseDetectedFolder)
            : new OutputTargetResult(null, OutputTargetStatus.NotWritable);
    }

    /// <summary>The folder that owns <paramref name="path"/>, or null when the path has no folder.</summary>
    public static string? SourceFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.IsNullOrEmpty(folder) ? null : folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Absolute path without a trailing separator, keeping a drive root as "D:\".</summary>
    private static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return normalized.Length > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
