namespace FileAction.Core;

/// <summary>One File Explorer shell window: the folder it shows and the handle of its tab window.</summary>
public readonly record struct ShellFolderCandidate(string Path, long WindowHandle);

/// <summary>
/// Chooses which shell window belongs to the tab under the cursor. Windows 11 lists every tab of one
/// Explorer window separately, all sharing the same top-level window, so the tab handle is the only
/// reliable discriminator. Pure logic so the choice can be unit tested without a desktop.
/// </summary>
public static class ShellFolderSelection
{
    /// <summary>Returned when no candidate can be attributed to the cursor.</summary>
    public const int None = -1;

    /// <param name="candidates">Shell windows that live under the top-level window under the cursor.</param>
    /// <param name="tabWindow">Handle of the tab window under the cursor, or 0 when the window has no tabs.</param>
    /// <param name="rootWindow">Handle of the top-level window under the cursor.</param>
    public static int Pick(
        IReadOnlyList<ShellFolderCandidate> candidates,
        long tabWindow,
        long rootWindow)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // 1. The tab that is visibly under the cursor.
        if (tabWindow != 0)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].WindowHandle == tabWindow)
                    return i;
            }
        }

        // 2. A window without tabs: the shell window is the top-level window itself.
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].WindowHandle == rootWindow)
                return i;
        }

        // 3. Only one shell window can possibly be meant.
        return candidates.Count == 1 ? 0 : None;
    }
}
