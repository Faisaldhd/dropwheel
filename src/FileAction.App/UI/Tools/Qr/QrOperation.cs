using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Qr;

/// <summary>
/// "Read QR code" on the Tools wheel. It applies to a single image and opens the reader window instead of
/// running a background job, so nothing is written and the link is only opened from inside the window.
/// One line in <c>WheelController</c>'s operation list (a shared file) registers it on the wheel.
/// </summary>
internal sealed class QrOperation : Operation, IInteractiveTool
{
    public QrOperation()
        : base("tools.qr", WheelKind.Tools, T("tool.qr.label"), Glyphs.Camera, T("tool.qr.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => QrTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(QrTool.Instance, items, targets).Show();
}
