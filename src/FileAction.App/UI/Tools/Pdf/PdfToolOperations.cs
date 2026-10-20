using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Pdf;

/// <summary>
/// The wheel operations these two tools contribute. The shared <c>WheelController.BuildOperations</c> list
/// spreads <see cref="All"/> after the Crop operation; keeping the pair here means that integration step is
/// one spread instead of two constructors.
/// </summary>
internal static class PdfToolOperations
{
    public static IReadOnlyList<Operation> All { get; } = [new PdfMergeOperation(), new PdfOrganizeOperation()];
}

/// <summary>
/// "Merge to PDF" on the Tools wheel. It applies to two or more PDFs and/or images and opens the merge
/// window instead of running a background job, so the user can choose the order before anything is
/// written. Registered on the wheel by <c>WheelController.BuildOperations</c>.
/// </summary>
internal sealed class PdfMergeOperation : Operation, IInteractiveTool
{
    public PdfMergeOperation()
        : base("tools.pdfmerge", WheelKind.Tools, T("tool.pdfmerge.label"), Glyphs.Merge, T("tool.pdfmerge.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => PdfMergeTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        var spec = ToolRegistry.For(items) ?? PdfMergeTool.Instance;
        new ToolWindow(spec, items, targets).Show();
    }
}

/// <summary>
/// "Organize Pages" on the Tools wheel. It applies to one PDF and opens the page grid instead of running
/// a background job. Registered on the wheel by <c>WheelController.BuildOperations</c>.
/// </summary>
internal sealed class PdfOrganizeOperation : Operation, IInteractiveTool
{
    public PdfOrganizeOperation()
        : base("tools.pdforganize", WheelKind.Tools, T("tool.pdforganize.label"), Glyphs.Page, T("tool.pdforganize.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => PdfOrganizeTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        var spec = ToolRegistry.For(items) ?? PdfOrganizeTool.Instance;
        new ToolWindow(spec, items, targets).Show();
    }
}
