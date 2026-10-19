using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>An operation whose "execution" is opening a floating tool window instead of a background job.</summary>
internal interface IInteractiveTool
{
    void Open(IReadOnlyList<FileItem> items, DropTargets? targets);
}
