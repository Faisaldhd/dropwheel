namespace FileAction.App.UI.Tools;

/// <summary>
/// Tracks whether a tool window is currently running its Apply action. While it is, the window must stay
/// open: letting it close would discard the only owner of the running write and could leave a partial file
/// at the final name. The state is UI-thread only.
/// </summary>
internal sealed class ToolApplyState
{
    public bool IsApplying { get; private set; }

    /// <summary>Starts one Apply. Returns false when one is already running (no re-entrant writes).</summary>
    public bool Begin()
    {
        if (IsApplying)
            return false;
        IsApplying = true;
        return true;
    }

    public void End() => IsApplying = false;

    /// <summary>True when a close request must be deferred until the running Apply finishes.</summary>
    public bool BlocksClose => IsApplying;
}
