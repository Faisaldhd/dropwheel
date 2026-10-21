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
    /// ~576 × 660 DIP: the three-column editor (label, value, remove button) needs a real minimum width,
    /// otherwise the remove buttons paint outside the rounded card. The sizer still clamps it to the work area.
    /// </summary>
    internal static readonly ToolSpecLayout Panel = new(
        ChromeWidth: 36,
        ChromeHeight: 140,
        PreviewMinWidth: 500,
        PreviewMinHeight: 340,
        PreviewMaxWidth: 660,
        PreviewMaxHeight: 700,
        DefaultPreviewWidth: 540,
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
