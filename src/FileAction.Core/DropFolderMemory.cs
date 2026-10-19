namespace FileAction.Core;

/// <summary>
/// The two folders a drop can target: the folder under the cursor when the wheel opened, and the last
/// usable folder remembered while the drag was in progress (used when the cursor is back on the source).
/// </summary>
public sealed record DropTargets(string? UnderCursor, string? Remembered);

/// <summary>
/// Remembers the last usable File Explorer folder seen while a drag is in progress, so a drop whose
/// cursor is back over the source (or over the wheel itself) still lands in the folder the user
/// crossed on the way. A folder observed before the current drag started is never used: the memory is
/// cleared when the drag begins.
/// </summary>
public sealed class DropFolderMemory
{
    private string? _remembered;

    /// <summary>The folder to fall back to during this drag, or null when none was seen.</summary>
    public string? Remembered => _remembered;

    /// <summary>Starts a new drag: forgets everything seen earlier.</summary>
    public void BeginDrag() => _remembered = null;

    /// <summary>
    /// Keeps <paramref name="folder"/> when it is a real, writable folder. Probes are injected so the
    /// rule is testable without a disk; an unusable folder never replaces a good remembered one.
    /// </summary>
    public void Observe(string? folder, Func<string, bool> directoryExists, Func<string, bool> canWrite)
    {
        ArgumentNullException.ThrowIfNull(directoryExists);
        ArgumentNullException.ThrowIfNull(canWrite);

        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            if (directoryExists(folder) && canWrite(folder))
                _remembered = folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A malformed path is simply not a folder we can offer.
        }
    }
}
