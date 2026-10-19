using System.Windows;
using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>
/// One floating tool: what it is called, how big it wants to be, which files it accepts and what its
/// window shows. A tool lives in its own file under <c>UI/Tools/&lt;Group&gt;/</c> and registers itself,
/// so adding a tool never means editing another tool's file.
/// </summary>
internal interface IToolWindowSpec
{
    /// <summary>Operation id, e.g. <c>tools.crop</c>.</summary>
    string Id { get; }

    /// <summary>Localization key for the window title.</summary>
    string TitleKey { get; }

    /// <summary>Declared size in DIPs (see <see cref="ToolSizes"/>).</summary>
    (double Width, double Height) Sizes { get; }

    /// <summary>True when the tool can open for these files.</summary>
    bool Applies(IReadOnlyList<FileItem> items);

    /// <summary>Builds the tool's view and its Reset / Apply actions.</summary>
    ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets);
}

/// <summary>The view and the two actions every tool window provides.</summary>
/// <param name="View">The tool-specific content between the title bar and the bottom buttons.</param>
/// <param name="Reset">Restores the tool's initial state.</param>
/// <param name="Apply">Runs the work and returns the created file, or null when nothing was written.</param>
internal sealed record ToolContent(FrameworkElement View, Action Reset, Func<Task<string?>> Apply);
