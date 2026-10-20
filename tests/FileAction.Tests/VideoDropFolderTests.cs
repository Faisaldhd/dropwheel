using FileAction.App.UI.Tools.Video;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// F-04 regression: Split and Join used to hardcode the source folder (and the first clip's folder),
/// so a drop from A onto B still wrote into A. Both views must resolve the detected/remembered folder
/// through the same helper Collage and Crop use.
/// </summary>
public sealed class VideoDropFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _drop;

    public VideoDropFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dropwheel-video-drop-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        _drop = Path.Combine(_root, "drop");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_drop);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Split_writes_into_the_detected_drop_folder()
    {
        var source = Path.Combine(_source, "clip.mp4");
        File.WriteAllBytes(source, []);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_drop)),
            VideoSplitView.OutputDirectoryFor(source, new DropTargets(_drop, null)));
    }

    [Fact]
    public void Split_falls_back_to_the_source_folder_without_a_drop_target()
    {
        var source = Path.Combine(_source, "clip.mp4");
        File.WriteAllBytes(source, []);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_source)),
            VideoSplitView.OutputDirectoryFor(source, new DropTargets(null, null)));
    }

    [Fact]
    public void Join_writes_into_the_detected_drop_folder()
    {
        var first = Path.Combine(_source, "one.mp4");
        File.WriteAllBytes(first, []);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_drop)),
            VideoJoinView.OutputDirectoryFor(first, new DropTargets(_drop, null)));
    }

    [Fact]
    public void Join_uses_the_remembered_folder_when_nothing_is_under_the_cursor()
    {
        var first = Path.Combine(_source, "one.mp4");
        File.WriteAllBytes(first, []);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_drop)),
            VideoJoinView.OutputDirectoryFor(first, new DropTargets(null, _drop)));
    }
}
