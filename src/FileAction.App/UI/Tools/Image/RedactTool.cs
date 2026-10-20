using System.Runtime.CompilerServices;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Redact a single image: draw one or more areas over anything private, hide each of them with a solid
/// fill, a blur or pixelation, and write a flattened copy next to the source. The original file is only
/// read, and the copy is never written over an existing file.
/// </summary>
/// <remarks>
/// Face detection is the one item of the brief this build does not ship. The app targets plain
/// <c>net10.0-windows</c> (no Windows SDK projection, see the csproj), so <c>Windows.Media.FaceAnalysis</c>
/// is not projected and reaching it would mean another raw-COM layer; a third-party detector would mean a
/// new package. Manual areas plus the Windows OCR text detection (which reuses the existing OCR engine) are
/// the shipped, offline, licence-clean feature set. Adding faces later needs no change to the area model
/// or the flatten plan: the detector only has to hand back more <c>RedactionBox</c>es. This is reported as
/// the documented follow-up with the pull request.
/// </remarks>
internal sealed class RedactTool : IToolWindowSpec
{
    public static readonly RedactTool Instance = new();

    /// <summary>
    /// The formats Magick can both read and write back under the same extension. SVG and HEIC are
    /// deliberately absent: they have no honest same-format writer here (same list as the crop tool).
    /// </summary>
    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "webp", "avif", "tiff", "bmp", "ico", "gif",
    };

    public string Id => "tools.redact";

    public string TitleKey => "tool.redact.title";

    /// <summary>The bottom-right action says what it does instead of the generic "Apply".</summary>
    public string PrimaryLabelKey => "tool.redact.save";

    /// <summary>
    /// 36 DIP of chrome around the preview, and ~290 DIP of fixed rows (title bar, mode row, strength or
    /// colour row, action row, hint and the bottom buttons). With the default preview that opens at
    /// 500 × 745; the sizer then follows the image's aspect ratio inside these bounds.
    /// </summary>
    public ToolSpecLayout Layout { get; } = new(
        ChromeWidth: 36,
        ChromeHeight: 290,
        PreviewMinWidth: 280,
        PreviewMinHeight: 260,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 464,
        DefaultPreviewHeight: 455);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new RedactView(items[0]).Build();
}

/// <summary>
/// "Redact" on the Tools wheel. Like Crop it opens a window instead of running a background job, and it
/// applies to exactly one image.
/// </summary>
/// <remarks>
/// The wheel builds its operations from a shared list (<c>WheelController.BuildOperations</c>), which does
/// not include this operation yet — nor the Annotate tool. That one line belongs to the shared integration
/// commit, so it is reported with the pull request instead of edited here; the operation, the window and
/// the strings are complete, and the operation opens its own window by identity.
/// </remarks>
internal sealed class RedactOperation : Operation, IInteractiveTool
{
    public RedactOperation()
        : base("tools.redact", WheelKind.Tools, T("tool.redact.label"), Glyphs.Clear, T("tool.redact.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => RedactTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    /// <summary>Opens this tool by identity; <c>ToolRegistry.For(items)</c> is first-match and ambiguous.</summary>
    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(RedactTool.Instance, items, targets).Show();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class RedactToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(RedactTool.Instance);
}
