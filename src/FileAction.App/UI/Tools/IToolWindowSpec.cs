using System.Windows;
using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>Media dimensions in pixels, reported once the tool has probed its input.</summary>
internal readonly record struct MediaSize(int Width, int Height);

/// <summary>
/// One floating tool: what it is called, the chrome and preview bounds it declares, which files it
/// accepts and what its window shows. A tool lives in its own file under <c>UI/Tools/&lt;Group&gt;/</c>
/// and registers itself, so adding a tool never means editing another tool's file.
/// </summary>
internal interface IToolWindowSpec
{
    /// <summary>Operation id, e.g. <c>tools.crop</c>.</summary>
    string Id { get; }

    /// <summary>Localization key for the window title.</summary>
    string TitleKey { get; }

    /// <summary>Localization key for the primary button; tools whose action is not "Apply" override it.</summary>
    string ApplyLabelKey => "tool.window.apply";

    /// <summary>Chrome height/width and preview min/max (see <see cref="ToolSizes"/>).</summary>
    ToolSpecLayout Layout { get; }

    /// <summary>True when the tool can open for these files.</summary>
    bool Applies(IReadOnlyList<FileItem> items);

    /// <summary>Builds the tool's view and its Reset / Apply actions.</summary>
    ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets);
}

/// <summary>The view, the two actions every tool window provides, and the async media probe.</summary>
/// <param name="View">The tool-specific content between the title bar and the bottom buttons.</param>
/// <param name="Reset">Restores the tool's initial state.</param>
/// <param name="Apply">Runs the work and returns the created file, or null when nothing was written.</param>
/// <param name="Ready">Completes with the media size once the downsampled preview is available.</param>
/// <param name="Activate">Swaps the placeholder for the loaded preview (runs on the UI thread).</param>
internal sealed record ToolContent(
    FrameworkElement View,
    Action Reset,
    Func<Task<string?>> Apply,
    Task<MediaSize?> Ready,
    Func<Task> Activate);
