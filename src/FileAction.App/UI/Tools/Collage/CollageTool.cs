using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Collage;

/// <summary>
/// Collage: two or more images arranged on one canvas (grid, row, column or a big featured first image),
/// with output size, spacing, corners, background and a drag-reorder filmstrip. Apply renders from the
/// original files, so the preview never limits the output resolution.
/// </summary>
internal sealed class CollageTool : IToolWindowSpec
{
    public static readonly CollageTool Instance = new();

    /// <summary>
    /// The tool's own chrome budget. The preview area is a fixed 16:9 box inside which the collage canvas
    /// is letterboxed, which keeps the window at about 560 × 745 while the many controls stay usable.
    /// </summary>
    public static readonly ToolSpecLayout DefaultLayout = new(
        ChromeWidth: 40,
        ChromeHeight: 455,
        PreviewMinWidth: 320,
        PreviewMinHeight: 180,
        PreviewMaxWidth: 520,
        PreviewMaxHeight: 520,
        DefaultPreviewWidth: 520,
        DefaultPreviewHeight: 290);

    /// <summary>
    /// The probe reports a 16:9 box at a normal photo size once the images are loaded. It is deliberately
    /// larger than the minimum, because <see cref="ToolWindowSizer"/> caps the preview at 200% of the
    /// reported media and a small probe would collapse the window.
    /// </summary>
    public static readonly MediaSize PreviewShape = new(1920, 1080);

    public string Id => "tools.collage";

    public string TitleKey => "tool.collage.title";

    public ToolSpecLayout Layout => DefaultLayout;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count >= 2 &&
        items.All(item => !item.IsDirectory && Formats.Images.Contains(item.Format));

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new CollageView(items).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class CollageToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(CollageTool.Instance);
}
