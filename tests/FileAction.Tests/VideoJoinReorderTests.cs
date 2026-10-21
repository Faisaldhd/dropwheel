using System.Runtime.ExceptionServices;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Tangerine's Join window lists the clips with an up/down control on every row; ours only had drag.
/// These tests pin the explicit reorder path (the drag gesture stays untouched) and its edge clamping.
/// </summary>
public class VideoJoinReorderTests
{
    [Fact]
    public void Move_clip_steps_in_bounds_and_keeps_the_other_clips_in_order() => RunSta(() =>
    {
        var view = new VideoJoinView(
            [
                new FileItem(@"C:\clips\one.mp4"),
                new FileItem(@"C:\clips\two.mp4"),
                new FileItem(@"C:\clips\three.mp4"),
            ],
            targets: null);
        _ = view.Build();

        Assert.Equal(["one.mp4", "two.mp4", "three.mp4"], view.ClipNames);

        view.MoveClip(0, +1);
        Assert.Equal(["two.mp4", "one.mp4", "three.mp4"], view.ClipNames);

        view.MoveClip(2, -1);
        Assert.Equal(["two.mp4", "three.mp4", "one.mp4"], view.ClipNames);

        // The ends are a no-op, not a wrap-around.
        view.MoveClip(0, -1);
        view.MoveClip(2, +1);
        Assert.Equal(["two.mp4", "three.mp4", "one.mp4"], view.ClipNames);
    });

    [Fact]
    public void Cards_offer_up_and_down_buttons_without_removing_drag_reorder()
    {
        var source = File.ReadAllText(Path.Combine(VideoFolder(), "VideoJoinTool.cs"));

        Assert.Contains("BuildMoveButton(\"\\uE70E\", T(\"tool.pdfmerge.moveUp\")", source, StringComparison.Ordinal);
        Assert.Contains("BuildMoveButton(\"\\uE70D\", T(\"tool.pdfmerge.moveDown\")", source, StringComparison.Ordinal);
        Assert.Contains("MoveClip(index, -1)", source, StringComparison.Ordinal);
        Assert.Contains("MoveClip(index, +1)", source, StringComparison.Ordinal);
        // The two paths share the model operation; drag is not replaced by the buttons.
        Assert.Contains("card.MouseLeftButtonDown += CardDown;", source, StringComparison.Ordinal);
        Assert.Contains("VideoSplitJoin.Move(_clips, index, target)", source, StringComparison.Ordinal);
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
        // The xUnit host runs below the test project; the reflection runner runs outside the repo and
        // only the app assembly's location reliably leads back to the solution.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory(), typeof(VideoJoinView).Assembly.Location })
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
