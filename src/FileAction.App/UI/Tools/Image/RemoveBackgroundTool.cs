using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Remove BG: one photo, a checkerboard preview of the cut-out, an automatic or explicit background key,
/// tolerance and feather sliders, and a PNG save that repeats the preview at the original resolution.
/// </summary>
internal sealed class RemoveBackgroundTool : IToolWindowSpec
{
    public static readonly RemoveBackgroundTool Instance = new();

    /// <summary>The same readable still formats as the other image tools; SVG/HEIC have no honest writer.</summary>
    private static readonly HashSet<string> Formats =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "avif", "tiff", "bmp", "ico", "gif" };

    public string Id => "tools.removebackground";

    public string TitleKey => "tool.removebackground.title";

    /// <summary>The primary action cuts the background out, so the button says that instead of "Apply".</summary>
    public string PrimaryLabelKey => "tool.removebackground.apply";

    /// <summary>
    /// Title bar + background row + two sliders + the hint, plus the shared bottom bar: about 300 DIP.
    /// The preview keeps the photo's aspect inside 320–640 DIP.
    /// </summary>
    public ToolSpecLayout Layout => new(
        ChromeWidth: 36,
        ChromeHeight: 300,
        PreviewMinWidth: 320,
        PreviewMinHeight: 240,
        PreviewMaxWidth: 640,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 500,
        DefaultPreviewHeight: 400);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new RemoveBackgroundView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class RemoveBackgroundToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(RemoveBackgroundTool.Instance);
}
