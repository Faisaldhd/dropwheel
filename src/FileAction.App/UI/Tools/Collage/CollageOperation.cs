using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Collage;

/// <summary>
/// "Collage" on the Tools wheel. It accepts two or more images and opens the collage tool window instead
/// of running a background job, so the arrangement can be chosen before anything is written.
/// </summary>
/// <remarks>
/// Shared wiring still required (not part of this change): add
/// <c>new UI.Tools.Collage.CollageOperation()</c> to the operation list in
/// <c>src/FileAction.App/Wheel/WheelController.cs</c>; that file belongs to the coordinator.
/// </remarks>
internal sealed class CollageOperation : Operation, IInteractiveTool
{
    public CollageOperation()
        : base("tools.collage", WheelKind.Tools, T("tool.collage.label"), Glyphs.Photo, T("tool.collage.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => CollageTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        // Open this tool by identity: ToolRegistry.For(items) is first-match and several tools accept a
        // single image, so a generic lookup could open another tool's window from the Collage slice.
        new ToolWindow(CollageTool.Instance, items, targets).Show();
    }
}
