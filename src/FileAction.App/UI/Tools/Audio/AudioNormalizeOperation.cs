using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Normalize" on the Tools wheel: opens the loudness window so the target and the predicted result are
/// chosen before a two-pass normalize writes anything.
/// </summary>
internal sealed class AudioNormalizeOperation : Operation, IInteractiveTool
{
    public AudioNormalizeOperation()
        : base("tools.audionormalize", WheelKind.Tools, T("tool.audionormalize.label"), Glyphs.Equalizer, T("tool.audionormalize.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AudioNormalizeTool.Instance.Applies(items);

    /// <summary>The wheel never reaches this: an interactive tool opens its window instead of a job.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    /// <summary>FFmpeg is required for both passes; the wheel offers the download when it is missing.</summary>
    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) =>
        tools.Has(ExternalTool.Ffmpeg);

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AudioNormalizeTool.Instance, items, targets).Show();
}
