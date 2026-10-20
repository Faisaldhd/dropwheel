using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// "Edit" on the Tools wheel. It applies to a single photo and opens the editor instead of running a
/// background job, so nothing is written until the user presses Apply.
/// </summary>
internal sealed class EditPhotosOperation : Operation, IInteractiveTool
{
    public EditPhotosOperation()
        : base("tools.editphotos", WheelKind.Tools, T("tool.editphotos.label"), Glyphs.Color, T("tool.editphotos.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => EditPhotosTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    /// <summary>
    /// Opens this tool's window directly. <see cref="ToolRegistry.For"/> returns the first registered tool
    /// that accepts the files, which is ambiguous once several tools accept one image; the operation knows
    /// which window it is.
    /// </summary>
    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(EditPhotosTool.Instance, items, targets).Show();
}
