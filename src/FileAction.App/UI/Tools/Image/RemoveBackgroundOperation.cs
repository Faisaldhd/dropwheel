using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// "Remove BG" on the Tools wheel. It applies to a single image and opens the floating window instead of
/// running a background job, so the key, tolerance and feather are chosen before anything is written.
/// The one-click <c>image.bg.remove</c> catalogue slice uses the shipped defaults for the same core.
/// </summary>
internal sealed class RemoveBackgroundOperation : Operation, IInteractiveTool
{
    public RemoveBackgroundOperation()
        : base("tools.removebackground", WheelKind.Tools, T("tool.removebackground.label"), Glyphs.Clear, T("tool.removebackground.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => RemoveBackgroundTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    /// <summary>Opens this tool by identity; a first-match registry lookup would be ambiguous.</summary>
    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(RemoveBackgroundTool.Instance, items, targets).Show();
}
