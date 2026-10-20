using System.IO;
using System.Runtime.CompilerServices;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Trim a single video on a timeline. One window per operation id; the spec registers itself so no shared
/// list has to be edited.
/// </summary>
internal sealed class VideoTrimTool : IToolWindowSpec
{
    public static readonly VideoTrimTool Instance = new();

    /// <summary>Timeline + time boxes take about 330 DIP of height around the preview.</summary>
    private static readonly ToolSpecLayout TrimLayout = new(
        ChromeWidth: 36,
        ChromeHeight: 330,
        PreviewMinWidth: 560,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 684,
        PreviewMaxHeight: 440,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 310);

    public string Id => "tools.videotrim";

    public string TitleKey => "tool.videotrim.title";

    public ToolSpecLayout Layout => TrimLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoTrimView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class VideoTrimToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoTrimTool.Instance);
}

/// <summary>
/// "Trim" on the Tools wheel. It applies to one video and opens the trim window instead of running a
/// background job, so the range is chosen before anything is written.
/// </summary>
internal sealed class VideoTrimOperation : Operation, IInteractiveTool
{
    public VideoTrimOperation()
        : base("tools.videotrim", WheelKind.Tools, T("tool.videotrim.label"), Glyphs.Split, T("tool.videotrim.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoTrimTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoTrimTool.Instance, items, targets).Show();
}
