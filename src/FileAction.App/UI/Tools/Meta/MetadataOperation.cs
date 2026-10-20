using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Meta;

/// <summary>
/// "Edit metadata" on the Tools wheel: applies to one image, audio/video file or PDF and opens the
/// metadata window, where every field is listed and a copy with the edited tags can be written.
/// </summary>
internal sealed class MetadataOperation : Operation, IInteractiveTool
{
    public MetadataOperation()
        : base("tools.metadata", WheelKind.Tools, T("tool.metadata.label"), Glyphs.Library, T("tool.metadata.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => MetadataTool.Instance.Applies(items);

    /// <summary>Images and PDFs are read in-process; audio and video need FFmpeg.</summary>
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
        new ToolWindow(MetadataTool.Instance, items, targets).Show();
}
