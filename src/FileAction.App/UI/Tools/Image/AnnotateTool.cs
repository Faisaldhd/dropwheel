using System.Runtime.CompilerServices;
using FileAction.App.UI.Tools;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Annotate a single image: the floating window draws arrows, lines, shapes, text, numbered callouts, a
/// magnifier, freehand pen/highlighter strokes and stamp images over a downsampled preview, then writes
/// the flattened image at its original resolution. All geometry is in original image pixels, so zooming
/// the window never moves a mark.
/// </summary>
internal sealed class AnnotateTool : IToolWindowSpec
{
    public static readonly AnnotateTool Instance = new();

    /// <summary>
    /// Chrome and preview bounds for the annotate window. The toolbar, actions and two property rows are
    /// the fixed chrome; the default is the familiar ~500×745 card, and the sizer adapts it to the photo's
    /// aspect afterwards. <see cref="ToolSizes"/> is shared, so this layout lives with its tool.
    /// </summary>
    public static readonly ToolSpecLayout AnnotateLayout = new(
        ChromeWidth: 48,
        ChromeHeight: 322,
        PreviewMinWidth: 320,
        PreviewMinHeight: 280,
        PreviewMaxWidth: 620,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 452,
        DefaultPreviewHeight: 423);

    private static readonly HashSet<string> Formats =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "avif", "tiff", "bmp", "ico", "gif" };

    public string Id => "tools.annotate";

    public string TitleKey => "tool.annotate.title";

    public ToolSpecLayout Layout => AnnotateLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AnnotateView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class AnnotateToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AnnotateTool.Instance);
}

/// <summary>
/// "Annotate" on the Tools wheel. It applies to a single image and opens the annotate tool window instead
/// of running a background job, so the user can mark the photo before anything is written.
/// </summary>
internal sealed class AnnotateOperation : Operation, IInteractiveTool
{
    /// <summary>Segoe MDL2 "Edit" (pencil); the wheel has no shared annotate glyph yet.</summary>
    private const string WheelGlyph = "\uE70F";

    public AnnotateOperation()
        : base("tools.annotate", WheelKind.Tools, T("tool.annotate.label"), WheelGlyph, T("tool.annotate.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AnnotateTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        // Open this tool by identity. ToolRegistry.For(items) is first-match and several image tools accept
        // a single JPEG, so a generic lookup from the Annotate slice could open another tool's window.
        new ToolWindow(AnnotateTool.Instance, items, targets).Show();
    }
}
