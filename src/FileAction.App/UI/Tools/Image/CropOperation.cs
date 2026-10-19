using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// "Crop" on the Tools wheel. It applies to a single image and opens the crop tool window instead of
/// running a background job, so the user can choose the rectangle before anything is written.
/// </summary>
internal sealed class CropOperation : Operation, IInteractiveTool
{
    public CropOperation()
        : base("tools.crop", WheelKind.Tools, T("tool.crop.label"), Glyphs.Crop, T("tool.crop.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => CropTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        var spec = ToolRegistry.For(items) ?? CropTool.Instance;
        new ToolWindow(spec, items, targets).Show();
    }
}
