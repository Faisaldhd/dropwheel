using System.IO;
using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>
/// Where a floating tool writes its result: the folder the drop resolved (the Explorer folder under the
/// cursor, or the one remembered during the drag) when it is usable, otherwise the source folder. Same
/// rules as the crop tool, in one place so every tool behaves the same.
/// </summary>
internal static class ToolOutputFolder
{
    public static string For(FileItem item, DropTargets? targets)
    {
        var detected = OutputTarget.Resolve(item.Path, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }

        return detected.Directory
            ?? OutputTarget.SourceFolder(item.Path)
            ?? Environment.CurrentDirectory;
    }
}
