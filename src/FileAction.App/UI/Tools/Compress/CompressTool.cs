using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Compress;

/// <summary>
/// The compress tool: one image, audio/video file or PDF, a mode (target size / percentage / preset), an
/// optional maximum long side for images, and an estimate of the result before anything is written.
/// </summary>
internal sealed class CompressTool : IToolWindowSpec
{
    public static readonly CompressTool Instance = new();

    /// <summary>
    /// ~460 × 620 DIP: the window is content, not media, so it opens at a fixed panel size and the
    /// shared sizer still clamps it to the work area of the monitor under the cursor.
    /// </summary>
    internal static readonly ToolSpecLayout Panel = new(
        ChromeWidth: 36,
        ChromeHeight: 140,
        PreviewMinWidth: 340,
        PreviewMinHeight: 300,
        PreviewMaxWidth: 560,
        PreviewMaxHeight: 620,
        DefaultPreviewWidth: 424,
        DefaultPreviewHeight: 480);

    public string Id => "tools.compress";

    public string TitleKey => "tool.compress.title";

    public ToolSpecLayout Layout => Panel;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Compression.KindOf(items[0].Format) != MediaKind.None;

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new CompressView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class CompressToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(CompressTool.Instance);
}
