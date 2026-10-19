namespace FileAction.Core;

/// <summary>What the wheel should promise about the destination before the drop.</summary>
public enum DropTargetLabelKind
{
    /// <summary>A different, usable folder was found.</summary>
    Folder,

    /// <summary>The result will go next to the source file.</summary>
    NextToFile,

    /// <summary>A folder was offered but cannot be written to; the result will go next to the source.</summary>
    Blocked,
}

/// <summary>The label the wheel shows: its kind, the full folder path and the folder name.</summary>
public readonly record struct DropTargetLabel(DropTargetLabelKind Kind, string? Folder, string? FolderName);

/// <summary>
/// Chooses the pre-drop destination label from the folder under the cursor, the folder remembered during
/// the drag and the source folder. Pure logic (probes injected) so the promise shown on the wheel is
/// unit tested and matches the rules the drop itself uses.
/// </summary>
public static class DropTargetLabels
{
    public static DropTargetLabel Choose(
        string? sourceFolder,
        string? cursorFolder,
        string? rememberedFolder,
        Func<string, bool> directoryExists,
        Func<string, bool> canWrite)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        ArgumentNullException.ThrowIfNull(canWrite);

        var source = Normalize(sourceFolder);
        var offered = new[] { Normalize(cursorFolder), Normalize(rememberedFolder) };

        foreach (var candidate in offered)
        {
            if (candidate is null || !directoryExists(candidate) || !canWrite(candidate))
                continue;
            if (source is not null && string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
                continue; // saving in the source folder is "next to the file", not a destination
            return new DropTargetLabel(DropTargetLabelKind.Folder, candidate, NameOf(candidate));
        }

        // A real folder was pointed at, but it refuses writes: say so instead of promising it.
        foreach (var candidate in offered)
        {
            if (candidate is not null && directoryExists(candidate) && !canWrite(candidate))
                return new DropTargetLabel(DropTargetLabelKind.Blocked, candidate, NameOf(candidate));
        }

        return new DropTargetLabel(DropTargetLabelKind.NextToFile, null, null);
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string NameOf(string folder)
    {
        var name = Path.GetFileName(folder);
        return string.IsNullOrEmpty(name) ? folder : name;
    }
}
