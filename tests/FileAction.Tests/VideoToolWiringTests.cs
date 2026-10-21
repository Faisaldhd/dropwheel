using System.Runtime.ExceptionServices;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Wiring regressions from 2.4.7: Apply is gated on actual captures/areas, and every video wheel slice
/// declares FFmpeg so a machine without it gets the same dimmed "get FFmpeg" prompt as Save frames
/// instead of opening a window that cannot do anything.
/// </summary>
public class VideoToolWiringTests
{
    [Fact]
    public void Frames_and_redact_roots_are_apply_gates_for_their_own_content()
    {
        var directory = Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Video");

        // ToolContent.View is the tool's root element, so the gate has to live on that Grid, not on the
        // plain view class that builds it.
        var common = File.ReadAllText(Path.Combine(directory, "VideoCommon.cs"));
        Assert.Contains("internal sealed class VideoGateGrid : Grid, IToolActionGate", common, StringComparison.Ordinal);
        Assert.Contains("CanApplyChanged?.Invoke();", common, StringComparison.Ordinal);

        var frames = File.ReadAllText(Path.Combine(directory, "VideoFramesTool.cs"));
        Assert.Contains("var root = new VideoGateGrid();", frames, StringComparison.Ordinal);
        Assert.Contains(
            "_root?.SetCanApply(_ffmpeg is not null && _info is not null && !_captures.IsEmpty);",
            frames,
            StringComparison.Ordinal);

        var redact = File.ReadAllText(Path.Combine(directory, "VideoRedactTool.cs"));
        Assert.Contains("var root = new VideoGateGrid();", redact, StringComparison.Ordinal);
        Assert.Contains(
            "_root?.SetCanApply(_ffmpeg is not null && _info is not null && _areas.Count > 0);",
            redact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_grid_raises_only_when_the_state_really_changes() => RunSta(() =>
    {
        var gate = new VideoGateGrid();
        var raised = 0;
        gate.CanApplyChanged += () => raised++;

        Assert.False(gate.CanApply);
        gate.SetCanApply(false);
        Assert.Equal(0, raised);

        gate.SetCanApply(true);
        Assert.True(gate.CanApply);
        Assert.Equal(1, raised);

        gate.SetCanApply(true);
        Assert.Equal(1, raised);

        gate.SetCanApply(false);
        Assert.False(gate.CanApply);
        Assert.Equal(2, raised);
    });

    [Fact]
    public void Every_video_tool_operation_requires_ffmpeg()
    {
        Operation[] operations =
        [
            new VideoFramesOperation(),
            new VideoRedactOperation(),
            new VideoTrimOperation(),
            new VideoCropOperation(),
            new VideoSpeedOperation(),
            new VideoSplitOperation(),
            new VideoJoinOperation(),
        ];

        Assert.All(operations, operation =>
            Assert.Contains(ExternalTool.Ffmpeg, operation.Requires));
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

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
