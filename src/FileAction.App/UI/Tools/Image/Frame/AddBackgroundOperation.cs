using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image.Frame;

/// <summary>
/// "Add BG" on the Tools wheel. It applies to a single photo and opens the Add Background window instead
/// of running a background job. It opens its own spec by id (not through <c>ToolRegistry.For</c>), so a
/// second image tool can never hijack another one's slice.
///
/// The wheel list is still shared code (<c>WheelController.BuildOperations</c>); registering this
/// operation there is the one integration step this tool needs.
/// </summary>
internal sealed class AddBackgroundOperation : Operation, IInteractiveTool
{
    public AddBackgroundOperation()
        : base("tools.addbackground", WheelKind.Tools, T("tool.addbackground.label"), Glyphs.Photo, T("tool.addbackground.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AddBackgroundTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AddBackgroundTool.Instance, items, targets).Show();
}
