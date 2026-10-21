using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// "Split" on the Tools wheel. Like Crop, it applies to the dropped items and opens a floating tool
/// window instead of starting a background job, so the split points are chosen before anything is
/// written. The two operations are listed in the app's own list (WheelController.BuildOperations), not
/// in the shared catalog.
/// </summary>
internal sealed class VideoSplitOperation : Operation, IInteractiveTool
{
    public VideoSplitOperation()
        : base("tools.videosplit", WheelKind.Tools, T("tool.videosplit.label"), Glyphs.Split, T("tool.videosplit.subtitle"))
    {
        Requires = [ExternalTool.Ffmpeg];
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoSplitTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoSplitTool.Instance, items, targets).Show();
}

/// <summary>"Join" on the Tools wheel: opens the join window for two or more videos.</summary>
internal sealed class VideoJoinOperation : Operation, IInteractiveTool
{
    public VideoJoinOperation()
        : base("tools.videojoin", WheelKind.Tools, T("tool.videojoin.label"), Glyphs.Merge, T("tool.videojoin.subtitle"))
    {
        Requires = [ExternalTool.Ffmpeg];
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoJoinTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoJoinTool.Instance, items, targets).Show();
}
