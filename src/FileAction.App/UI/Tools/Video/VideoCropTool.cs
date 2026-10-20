using System.Runtime.CompilerServices;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>Crop a single video frame; one window per operation id, registered in its own file.</summary>
internal sealed class VideoCropTool : IToolWindowSpec
{
    public static readonly VideoCropTool Instance = new();

    /// <summary>Chips, W/H boxes and the scrub strip take about 340 DIP around the preview.</summary>
    private static readonly ToolSpecLayout CropLayout = new(
        ChromeWidth: 36,
        ChromeHeight: 340,
        PreviewMinWidth: 560,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 684,
        PreviewMaxHeight: 430,
        DefaultPreviewWidth: 684,
        DefaultPreviewHeight: 310);

    public string Id => "tools.videocrop";

    public string TitleKey => "tool.videocrop.title";

    public ToolSpecLayout Layout => CropLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Video.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new VideoCropView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class VideoCropToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(VideoCropTool.Instance);
}

/// <summary>"Crop" on the Tools wheel: opens the crop window instead of running a background job.</summary>
internal sealed class VideoCropOperation : Operation, IInteractiveTool
{
    public VideoCropOperation()
        : base("tools.videocrop", WheelKind.Tools, T("tool.videocrop.label"), Glyphs.Crop, T("tool.videocrop.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => VideoCropTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(VideoCropTool.Instance, items, targets).Show();
}
