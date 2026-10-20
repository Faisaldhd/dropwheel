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

    /// <summary>The primary action writes a new file, so the button names that instead of "Apply".</summary>
    public string PrimaryLabelKey => "tool.addbackground.apply";

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
/// Registers the tool from its own file, so no shared list has to be edited. Registration order no
/// longer decides which image tool opens: the wheel names its tool by id
/// (<see cref="ToolRegistry.For(string, IReadOnlyList{FileItem})"/>) and this tool's operation opens it
/// directly.
/// </summary>
internal static class AddBackgroundRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(AddBackgroundTool.Instance);
}
