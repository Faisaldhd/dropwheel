using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Compress;

/// <summary>
/// "Compress" on the Tools wheel: applies to one image, audio/video file or PDF and opens the compress
/// window, where the user picks the mode and sees the estimate before a copy is written.
/// </summary>
internal sealed class CompressOperation : Operation, IInteractiveTool
{
    public CompressOperation()
        : base("tools.compress", WheelKind.Tools, T("tool.compress.label"), Glyphs.Shrink, T("tool.compress.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => CompressTool.Instance.Applies(items);

    /// <summary>Images and PDFs are handled in-process; audio and video need FFmpeg.</summary>
    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items)
    {
        if (items.Count != 1 || items[0].IsDirectory)
            return false;
        var kind = Compression.KindOf(items[0].Format);
        return kind is MediaKind.Audio or MediaKind.Video ? tools.Has(ExternalTool.Ffmpeg) : kind != MediaKind.None;
    }

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(CompressTool.Instance, items, targets).Show();
}
