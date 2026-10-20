using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// "Trim" on the Tools wheel: opens the waveform window so the exact start and end are chosen before the
/// cut is written.
/// </summary>
internal sealed class AudioTrimOperation : Operation, IInteractiveTool
{
    public AudioTrimOperation()
        : base("tools.audiotrim", WheelKind.Tools, T("tool.audiotrim.label"), Glyphs.Stopwatch, T("tool.audiotrim.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AudioTrimTool.Instance.Applies(items);

    /// <summary>The wheel never reaches this: an interactive tool opens its window instead of a job.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    /// <summary>FFmpeg decodes the waveform and writes the selection.</summary>
    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) =>
        tools.Has(ExternalTool.Ffmpeg);

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AudioTrimTool.Instance, items, targets).Show();
}
