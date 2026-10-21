using System.Runtime.CompilerServices;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>Change a single video's speed; one window per operation id, registered in its own file.</summary>
internal sealed class VideoSpeedTool : IToolWindowSpec
{
    public static readonly VideoSpeedTool Instance = new();

    /// <summary>Slider, presets and the strip take about 310 DIP around the preview.</summary>
    private static readonly ToolSpecLayout SpeedLayout = new(
        ChromeWidth: 36,
        ChromeHeight: 310,
        PreviewMinWidth: 560,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 684,
        PreviewMaxHeight: 440,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 310);

    public string Id => "tools.videospeed";

    public string TitleKey => "tool.videospeed.title";

    public ToolSpecLayout Layout => SpeedLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoSpeedView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class VideoSpeedToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoSpeedTool.Instance);
}

/// <summary>"Speed" on the Tools wheel: opens the speed window instead of running a background job.</summary>
internal sealed class VideoSpeedOperation : Operation, IInteractiveTool
{
    public VideoSpeedOperation()
        : base("tools.videospeed", WheelKind.Tools, T("tool.videospeed.label"), Glyphs.Stopwatch, T("tool.videospeed.subtitle"))
    {
        Requires = [ExternalTool.Ffmpeg];
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoSpeedTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoSpeedTool.Instance, items, targets).Show();
}
