using System.IO;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Video;

/// <summary>Where the three video tools write their results: next to the source, or into the drop folder.</summary>
internal static class VideoOutput
{
    public static string NameFor(FileItem item, string suffix) => $"{item.BaseName} {suffix}";

    /// <summary>Next to the source, or the folder this drop resolved (the 2.2.3 drop-target rules).</summary>
    public static string DirectoryFor(FileItem item, DropTargets? targets)
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
