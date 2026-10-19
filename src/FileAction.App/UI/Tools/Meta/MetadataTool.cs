using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Meta;

/// <summary>
/// The metadata tool: every field of one image, audio/video file or PDF, grouped by file / track /
/// chapter, with a search box and an editor for camera, location, tags, chapters and the PDF info block.
/// </summary>
internal sealed class MetadataTool : IToolWindowSpec
{
    public static readonly MetadataTool Instance = new();

    /// <summary>
    /// ~536 × 660 DIP: a field list and a two-column editor need the height. The shared sizer still clamps
    /// it to the work area of the monitor under the cursor.
    /// </summary>
    internal static readonly ToolSpecLayout Panel = new(
        ChromeWidth: 36,
        ChromeHeight: 140,
        PreviewMinWidth: 360,
        PreviewMinHeight: 340,
        PreviewMaxWidth: 620,
        PreviewMaxHeight: 700,
        DefaultPreviewWidth: 500,
        DefaultPreviewHeight: 520);

    public string Id => "tools.metadata";

    public string TitleKey => "tool.metadata.title";

    /// <summary>The bottom-right button of this tool reads "Save", not the shared "Apply".</summary>
    public string PrimaryLabelKey => "tool.metadata.save";

    public ToolSpecLayout Layout => Panel;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Compression.KindOf(items[0].Format) != MediaKind.None;

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new MetadataView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class MetadataToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(MetadataTool.Instance);
}
