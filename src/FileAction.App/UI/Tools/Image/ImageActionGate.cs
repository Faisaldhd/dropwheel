using System.Windows.Controls;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The root of an image tool's window. <see cref="ToolContent.View"/> is this element (not the view
/// class), so the shared window discovers the gate here and keeps the primary button disabled until
/// <see cref="Ready"/> is called by the decoder.
/// </summary>
internal sealed class ImageGateGrid : Grid, IToolActionGate
{
    private readonly ImageActionGate _gate = new();

    public bool CanApply => _gate.CanApply;

    public event Action? CanApplyChanged
    {
        add => _gate.CanApplyChanged += value;
        remove => _gate.CanApplyChanged -= value;
    }

    /// <summary>Marks the image as loaded and repaints the primary button once.</summary>
    public void Ready() => _gate.Ready();
}

/// <summary>
/// The primary-action gate shared by the image tools. It starts closed, so the window's Apply button is
/// disabled while the preview decodes, and opens once when the decoder has produced a usable image. A
/// failed decode never opens it, so Apply cannot run against a missing image.
/// </summary>
internal sealed class ImageActionGate
{
    private bool _ready;

    public bool CanApply => _ready;

    public event Action? CanApplyChanged;

    public void Ready()
    {
        if (_ready)
            return;
        _ready = true;
        CanApplyChanged?.Invoke();
    }
}
