using System.Runtime.ExceptionServices;
using System.Windows;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The shared VideoPreview owns one play/pause control: Crop and Speed inherit it, Trim keeps its own
/// external button and suppresses the internal one, and unsupported containers fail to scrub-only.
/// </summary>
public class VideoPreviewPlaybackTests
{
    [Fact]
    public void Playback_is_offered_for_every_video_format()
    {
        foreach (var format in new[] { "mp4", "MP4", "mov", "m4v", "wmv", "avi", "mkv", "webm", "flv", "ts", "3gp" })
            Assert.True(VideoEditPreview.IsPlayableFormat(format), format);

        Assert.False(VideoEditPreview.IsPlayableFormat(""));
        Assert.False(VideoEditPreview.IsPlayableFormat(null));
    }

    [Fact]
    public void Shared_play_control_and_note_start_hidden() => RunSta(() =>
    {
        using var preview = new VideoEditPreview(new FileItem(@"C:\videos\clip.mp4"), "tool.videocrop");

        Assert.Equal(Visibility.Collapsed, preview.PlaybackButton.Visibility);
        Assert.Equal(Visibility.Collapsed, preview.PlaybackNote.Visibility);
        Assert.False(string.IsNullOrWhiteSpace(preview.PlaybackNoteText.Text));

        // The surface stays exactly what the tools draw on; the control is an extra child, not a replacement.
        Assert.True(preview.Surface.Children.Contains(preview.PlaybackButton));
        Assert.True(preview.Surface.Children.Contains(preview.PlaybackNote));
    });

    [Fact]
    public void Disposing_a_preview_stops_playback_and_is_safe() => RunSta(() =>
    {
        var preview = new VideoEditPreview(new FileItem(@"C:\videos\clip.mp4"), "tool.videospeed");
        preview.Dispose();

        Assert.False(preview.IsPlaying);
        Assert.Equal(Visibility.Collapsed, preview.PlaybackButton.Visibility);
    });

    [Fact]
    public void The_control_is_shared_and_trim_keeps_its_external_button()
    {
        var folder = VideoFolder();
        var preview = File.ReadAllText(Path.Combine(folder, "VideoPreview.cs"));
        Assert.Contains("ToggleFromControlAsync", preview, StringComparison.Ordinal);
        Assert.Contains("EnablePlaybackCoreAsync(TimeSpan.Zero, Info.Duration)", preview, StringComparison.Ordinal);
        Assert.Contains("_externalPlayback = true;", preview, StringComparison.Ordinal);

        foreach (var view in new[] { "VideoCropView.cs", "VideoSpeedView.cs" })
        {
            var source = File.ReadAllText(Path.Combine(folder, view));
            Assert.Contains("new VideoEditPreview(", source, StringComparison.Ordinal);
            // No per-tool play button: the shared preview supplies it.
            Assert.DoesNotContain("tool.videotrim.play", source, StringComparison.Ordinal);
        }

        var trim = File.ReadAllText(Path.Combine(folder, "VideoTrimView.cs"));
        Assert.Contains("_preview.EnablePlaybackAsync(_start, _end)", trim, StringComparison.Ordinal);
    }

    /// <summary>WPF controls need an STA thread; xUnit runs tests on MTA.</summary>
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string VideoFolder()
    {
        // The xUnit host runs below the test project; the local reflection runner runs below _local.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
                    return Path.Combine(directory.FullName, "src", "FileAction.App", "UI", "Tools", "Video");
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("FileAction.sln not found above the test or working directory.");
    }
}
