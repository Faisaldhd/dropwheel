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
/// The wheel builds its operations from a shared list, so this operation still has to be added next to
/// <c>CropOperation</c> in <c>WheelController.BuildOperations()</c>. That file is shared with the other
/// wave branches, so the one-line wiring is reported with the pull request instead of edited here; the
/// operation, the window and the strings are complete.
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

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(RedactTool.Instance, items, targets).Show();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class RedactToolRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        // ToolRegistry.For(items) answers "which window tool handles these files" with the first matching
        // registration, and two image tools now match a single image. Registering the crop tool here keeps
        // its slice opening the crop window regardless of module-initializer order; the durable fix is for
        // the registry to take a tool id (reported with this pull request). Register is idempotent.
        ToolRegistry.Register(CropTool.Instance);
        ToolRegistry.Register(RedactTool.Instance);
    }
}
