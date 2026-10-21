using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The captured-frame tray used to be a row of frame numbers. Tangerine's tray is a thumbnail with the
/// frame's own timecode; these tests pin that shape and that Reset really forgets the old images.
/// </summary>
public class VideoFramesChipTests
{
    [Fact]
    public void Capture_chip_shows_the_frames_timecode_and_thumbnail() => RunSta(() =>
    {
        var view = new VideoFramesView(new FileItem(@"C:\videos\clip.mp4"), targets: null);
        SetField(view, "_info", new FileAction.App.UI.Tools.Video.VideoInfo(10, 30, 640, 360));

        var thumb = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, palette: null, new byte[16], stride: 8);
        thumb.Freeze();
        Thumbs(view)[123] = thumb;

        var chip = BuildChip(view, 123);

        Assert.Equal(96, chip.Width, 3);
        Assert.Equal(60, chip.Height, 3);
        Assert.Contains("00:00:04.100", Descendants<TextBlock>(chip).Select(text => text.Text));
        Assert.Same(thumb, Descendants<Image>(chip).Single().Source);
    });

    [Fact]
    public void Reset_forgets_the_captures_and_their_thumbnails() => RunSta(() =>
    {
        var view = new VideoFramesView(new FileItem(@"C:\videos\clip.mp4"), targets: null);
        SetField(view, "_info", new FileAction.App.UI.Tools.Video.VideoInfo(10, 30, 640, 360));
        var captures = (CaptureList)GetField(view, "_captures")!;
        captures.Add(12);
        Thumbs(view)[12] = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, palette: null, new byte[16], stride: 8);

        Invoke(view, "Reset");

        Assert.True(captures.IsEmpty);
        Assert.Empty(Thumbs(view));
    });

    [Fact]
    public void Capture_remembers_the_frame_that_is_actually_on_screen()
    {
        var source = File.ReadAllText(Path.Combine(VideoFolder(), "VideoFramesTool.cs"));

        Assert.Contains("RememberThumbnail(_frame);", source, StringComparison.Ordinal);
        Assert.Contains("_displayed = frame;", source, StringComparison.Ordinal);
        // A capture made before the preview arrived is filled in when that seek completes.
        Assert.Contains("_captures.Contains(frame) && !_thumbs.ContainsKey(frame)", source, StringComparison.Ordinal);
        Assert.Contains("FrameMath.Timecode(frame, _info.Fps)", source, StringComparison.Ordinal);
    }

    private static Border BuildChip(VideoFramesView view, int frame)
    {
        var method = typeof(VideoFramesView).GetMethod(
            "BuildCaptureChip", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Border)method.Invoke(view, [frame])!;
    }

    private static Dictionary<int, BitmapSource> Thumbs(VideoFramesView view) =>
        (Dictionary<int, BitmapSource>)GetField(view, "_thumbs")!;

    private static object? GetField(VideoFramesView view, string name) =>
        typeof(VideoFramesView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);

    private static void SetField(VideoFramesView view, string name, object value) =>
        typeof(VideoFramesView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, value);

    private static void Invoke(VideoFramesView view, string name) =>
        typeof(VideoFramesView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
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
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), typeof(VideoFramesView).Assembly.Location })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
                directory = directory.Parent;
            if (directory is not null)
                return Path.Combine(directory.FullName, "src", "FileAction.App", "UI", "Tools", "Video");
        }

        throw new DirectoryNotFoundException("FileAction.sln not found above the test or app assembly.");
    }
}
