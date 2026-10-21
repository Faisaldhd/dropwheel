using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The video Redact window lets the pointer select, move and resize a timed box. The rectangle rules come
/// from the shared <see cref="RedactionEditor"/>; these tests pin the video-specific adapter that keeps the
/// time range and style intact and rounds the result back to whole image pixels.
/// </summary>
public class VideoRedactEditorTests
{
    [Fact]
    public void Hit_test_returns_the_topmost_timed_area()
    {
        var areas = new List<VideoRedactionArea>
        {
            new(0, 0, 100, 100, 0, 2, VideoRedactionStyle.Solid),
            new(20, 20, 30, 30, 1, 3, VideoRedactionStyle.Blur),
        };

        Assert.Equal(1, VideoRedactionEditor.HitTest(areas, 30, 30));
        Assert.Equal(0, VideoRedactionEditor.HitTest(areas, 80, 80));
        Assert.Equal(-1, VideoRedactionEditor.HitTest(areas, 200, 200));
        Assert.Equal(-1, VideoRedactionEditor.HitTest([], 10, 10));
    }

    [Fact]
    public void Moving_a_box_keeps_its_time_range_and_style()
    {
        var area = new VideoRedactionArea(10, 10, 50, 40, 1.5, 3.5, VideoRedactionStyle.Blur, "#112233");

        var moved = VideoRedactionEditor.Move(area, -100, 1000, 200, 200);

        Assert.Equal(0, moved.X);
        Assert.Equal(160, moved.Y);
        Assert.Equal(50, moved.Width);
        Assert.Equal(40, moved.Height);
        Assert.Equal(1.5, moved.StartSeconds, 10);
        Assert.Equal(3.5, moved.EndSeconds, 10);
        Assert.Equal(VideoRedactionStyle.Blur, moved.Style);
        Assert.Equal("#112233", moved.Color);
    }

    [Fact]
    public void Resizing_from_a_handle_keeps_the_time_range_and_style()
    {
        var area = new VideoRedactionArea(100, 100, 200, 100, 2, 4, VideoRedactionStyle.Solid, "red");

        var resized = VideoRedactionEditor.Resize(
            area, RedactionHandle.BottomRight, 50, 25, 800, 600);

        Assert.Equal(100, resized.X);
        Assert.Equal(100, resized.Y);
        Assert.Equal(250, resized.Width);
        Assert.Equal(125, resized.Height);
        Assert.Equal(2, resized.StartSeconds, 10);
        Assert.Equal(4, resized.EndSeconds, 10);
        Assert.Equal(VideoRedactionStyle.Solid, resized.Style);
        Assert.Equal("red", resized.Color);
    }

    [Fact]
    public void A_dragged_box_becomes_whole_pixels_with_its_range_and_style()
    {
        var box = new RedactionBox(10.2, 20.4, 30.2, 40.4);

        var area = VideoRedactionEditor.Create(
            box, 1.25, 7.5, VideoRedactionStyle.Blur, "#aabbcc", 640, 480);

        Assert.Equal(10, area.X);
        Assert.Equal(20, area.Y);
        Assert.Equal(30, area.Width);
        Assert.Equal(41, area.Height);
        Assert.Equal(1.25, area.StartSeconds, 10);
        Assert.Equal(7.5, area.EndSeconds, 10);
        Assert.Equal(VideoRedactionStyle.Blur, area.Style);
        Assert.Equal("#aabbcc", area.Color);
    }

    [Fact]
    public void Editing_clamps_a_box_to_the_frame_and_never_leaves_it_empty()
    {
        var area = new VideoRedactionArea(60, 60, 20, 20, 0, 1, VideoRedactionStyle.Solid);

        var moved = VideoRedactionEditor.Move(area, 500, 500, 100, 80);

        Assert.Equal(80, moved.X);
        Assert.Equal(60, moved.Y);
        Assert.Equal(20, moved.Width);
        Assert.Equal(20, moved.Height);

        var resized = VideoRedactionEditor.Resize(
            area, RedactionHandle.BottomRight, -500, -500, 100, 80);

        Assert.True(resized.Width >= (int)RedactionEditor.MinSize);
        Assert.True(resized.Height >= (int)RedactionEditor.MinSize);
        Assert.True(resized.X + resized.Width <= 100);
        Assert.True(resized.Y + resized.Height <= 80);
    }

    [Fact]
    public void The_window_wires_the_pointer_gestures_to_its_handles()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Video", "VideoRedactTool.cs"));

        Assert.Contains("RedactionEditor.HandleAt(", source, StringComparison.Ordinal);
        Assert.Contains("VideoRedactionEditor.HitTest(", source, StringComparison.Ordinal);
        Assert.Contains("VideoRedactionEditor.Move(", source, StringComparison.Ordinal);
        Assert.Contains("VideoRedactionEditor.Resize(", source, StringComparison.Ordinal);
        Assert.Contains("_canvas.LostMouseCapture", source, StringComparison.Ordinal);
        Assert.Contains("PaintDraft();", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
