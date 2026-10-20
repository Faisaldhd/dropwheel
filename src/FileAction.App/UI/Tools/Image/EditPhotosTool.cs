using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Edit a single photo: light, colour, detail and effect sliders with a live, downsampled preview, a
/// hold-to-compare original, and Apply writing a new file next to the source. The parameters and the
/// Magick.NET mapping live in <see cref="PhotoEdit"/>; this class only declares the window.
/// </summary>
internal sealed class EditPhotosTool : IToolWindowSpec
{
    public static readonly EditPhotosTool Instance = new();

    /// <summary>Still-image formats this tool opens. GIF and ICO are left to Crop: editing them would
    /// silently flatten an animation or replace an icon's frames.</summary>
    private static readonly HashSet<string> Formats =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "png", "webp", "heic", "tiff", "avif", "bmp" };

    /// <summary>
    /// Default window 500×745 DIP: 440 DIP of preview plus 60 of side margin, 225 of preview plus 520 of
    /// chrome (title bar, the six-row slider panel of the Light tab and the bottom bar). The preview then
    /// follows the photo's own aspect inside its min/max bounds, and the sizer clamps the whole window to
    /// the monitor. Portrait photos keep a 360 DIP wide panel, so the sliders stay usable.
    /// </summary>
    public static readonly ToolSpecLayout ToolLayout = new(
        ChromeWidth: 60,
        ChromeHeight: 520,
        PreviewMinWidth: 360,
        PreviewMinHeight: 160,
        PreviewMaxWidth: 440,
        PreviewMaxHeight: 380,
        DefaultPreviewWidth: 440,
        DefaultPreviewHeight: 225);

    public string Id => "tools.editphotos";

    public string TitleKey => "tool.editphotos.title";

    /// <summary>The bottom-right button saves a copy; it never overwrites the photo.</summary>
    public string PrimaryLabelKey => "tool.editphotos.apply";

    public ToolSpecLayout Layout => ToolLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new EditPhotosView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class EditPhotosToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(EditPhotosTool.Instance);
}
