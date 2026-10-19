using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image.Frame;

/// <summary>
/// Add Background: one photo, a live preview of the photo over a gradient, colour or image backdrop,
/// and a save that repeats the same composition at the original resolution. The window itself is the
/// shared floating chrome; everything specific to the tool lives in <see cref="AddBackgroundView"/>.
/// </summary>
internal sealed class AddBackgroundTool : IToolWindowSpec
{
    public static readonly AddBackgroundTool Instance = new();

    private static readonly HashSet<string> Formats =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "avif", "tiff", "bmp", "ico", "gif" };

    public string Id => "tools.addbackground";

    public string TitleKey => "tool.addbackground.title";

    /// <summary>
    /// Chrome: the title bar, the two swatch rows, the image row, three sliders, the ratio chips and the
    /// bottom bar, which is about 350 DIP. The preview keeps the composition's aspect inside 380–520 wide.
    /// </summary>
    public ToolSpecLayout Layout => new(
        ChromeWidth: 36,
        ChromeHeight: 350,
        PreviewMinWidth: 380,
        PreviewMinHeight: 300,
        PreviewMaxWidth: 520,
        PreviewMaxHeight: 430,
        DefaultPreviewWidth: 464,
        DefaultPreviewHeight: 395);

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new AddBackgroundView(items[0], targets).Build();
}

/// <summary>
/// Registers the tool from its own file. The file lives in the Frame folder on purpose: module
/// initializers run in compile order, and this one must come after the crop tool so that
/// <c>ToolRegistry.For</c> (still a first-match lookup) keeps resolving a plain photo to Crop until the
/// wheel dispatch names its tool by id.
/// </summary>
internal static class AddBackgroundRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AddBackgroundTool.Instance);
}
