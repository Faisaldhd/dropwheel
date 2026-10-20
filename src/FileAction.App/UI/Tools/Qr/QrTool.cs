using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Qr;

/// <summary>
/// Read a QR code from a single image, entirely offline. The tool only ever reads the source file: the
/// decoded text is shown (and can be copied), and a link is opened in the browser only when the user
/// clicks Open. There is no upload, no telemetry and no automatic network access of any kind.
/// </summary>
internal sealed class QrTool : IToolWindowSpec
{
    public static readonly QrTool Instance = new();

    /// <summary>Title bar + status line, details, result box and the Copy/Open row, added to the preview.</summary>
    private static readonly ToolSpecLayout QrLayout = new(
        ChromeWidth: 36,
        ChromeHeight: 320,
        PreviewMinWidth: 300,
        PreviewMinHeight: 220,
        PreviewMaxWidth: 720,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 360,
        DefaultPreviewHeight: 300);

    public string Id => "tools.qr";

    public string TitleKey => "tool.qr.title";

    /// <summary>
    /// The window is read-only: the bottom-right action copies the decoded text and closes. Naming it
    /// "Copy and Close" keeps it from looking like an "Apply" that would write a file.
    /// </summary>
    public string PrimaryLabelKey => "tool.qr.copyClose";

    public ToolSpecLayout Layout => QrLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Images.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new QrView(items[0]).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class QrToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(QrTool.Instance);
}
