using FileAction.App.UI.Tools.Video;
using FileAction.Core;
using Xunit;
using AppProbe = FileAction.App.UI.Tools.Video.VideoProbe;
using CoreProbe = FileAction.Core.VideoProbe;

namespace FileAction.Tests;

/// <summary>
/// Regression tests against the owner's real sample clips: clip.mkv (2.021 s container, 48 frames at
/// 24 fps, no later keyframe), clip.mp4 (2 s, 48 frames) and tangerine-shift-convert.mp4 (40 s of video
/// and 2400 frames at 60 fps, wrapped in a 40.055 s container because of the audio tail). These are the
/// files that exposed the keyframe roll and the rounded-up frame count.
/// </summary>
[Trait("Category", "Integration")]
public sealed class VideoRealMediaTests
{
    [FfmpegFact]
    public async Task Real_clips_report_their_exact_packet_count()
    {
        if (VideoToolTestTools.FindFfmpeg() is not { } ffmpeg)
            return;

        foreach (var (name, expected) in new[]
                 {
                     ("clip.mkv", 48),
                     ("clip.mp4", 48),
                     ("tangerine-shift-convert.mp4", 2400),
                 })
        {
            if (Fixture(name) is not { } path)
                continue;

            var info = await AppProbe.ReadAsync(ffmpeg, path, default);
            Assert.NotNull(info);
            Assert.Equal(expected, info!.FrameCount);
            Assert.Equal(expected, info.TotalFrames);
            // clip.mkv's banner duration is the 2.021 s container length, while its video track holds
            // exactly 2.0 s; the packet count is what must win.
            Assert.True(info.Fps >= 24);
        }
    }

    [FfmpegFact]
    public async Task Fast_copy_of_mkv_does_not_keep_the_keyframe_pre_roll()
    {
        if (VideoToolTestTools.FindFfmpeg() is not { } ffmpeg || Fixture("clip.mkv") is not { } source)
            return;

        var outputDirectory = OutputPaths.TempDirectory();
        try
        {
            var output = await VideoToolRunner.TrimAsync(
                ffmpeg,
                new VideoTrimRequest(
                    source,
                    string.Empty,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    "mkv",
                    VideoTrimMode.StreamCopy,
                    HasAudio: true),
                outputDirectory,
                "clip Trimmed",
                default);

            var info = await CoreProbe.ReadAsync(ffmpeg, output, default);
            Assert.NotNull(info);
            // The copy can only start at the frame-0 keyframe and measured 2.021 s before the runner
            // verified it; the fallback re-encode writes the requested 1 s range instead.
            Assert.InRange(Math.Abs(info!.Duration.TotalSeconds - 1.0), 0.0, 0.12);
            Assert.True(info.HasAudio);
        }
        finally
        {
            OutputPaths.TryDelete(outputDirectory);
        }
    }

    [FfmpegFact]
    public async Task The_exact_last_frame_exports_for_every_real_clip()
    {
        if (VideoToolTestTools.FindFfmpeg() is not { } ffmpeg)
            return;

        foreach (var name in new[] { "clip.mkv", "clip.mp4", "tangerine-shift-convert.mp4" })
        {
            if (Fixture(name) is not { } path)
                continue;

            var info = await AppProbe.ReadAsync(ffmpeg, path, default);
            if (info is null)
                continue;

            var output = OutputPaths.TempDirectory();
            try
            {
                var folder = await VideoRunner.ExportFramesAsync(
                    ffmpeg,
                    new FileItem(path),
                    [info.TotalFrames - 1],
                    info.Fps,
                    output,
                    new Progress<(int Done, int Total)>(),
                    default,
                    info.DurationSeconds);

                var file = Assert.Single(Directory.GetFiles(folder, "*.png"));
                Assert.True(new FileInfo(file).Length > 0);
            }
            finally
            {
                OutputPaths.TryDelete(output);
            }
        }
    }

    [FfmpegFact]
    public async Task A_duration_based_phantom_frame_still_exports_the_last_picture()
    {
        if (VideoToolTestTools.FindFfmpeg() is not { } ffmpeg
            || Fixture("tangerine-shift-convert.mp4") is not { } path)
        {
            return;
        }

        var info = await AppProbe.ReadAsync(ffmpeg, path, default);
        if (info is null)
            return;

        // 40.055 s × 60 fps floors to 2403; the real packet count is 2400. Exporting the duration-based
        // last index used to abort the whole batch because FFmpeg found no frame past the last picture.
        var phantom = FrameMath.TotalFrames(info.DurationSeconds, info.Fps) - 1;
        if (phantom <= info.TotalFrames - 1)
            return;

        var output = OutputPaths.TempDirectory();
        try
        {
            var folder = await VideoRunner.ExportFramesAsync(
                ffmpeg,
                new FileItem(path),
                [phantom],
                info.Fps,
                output,
                new Progress<(int Done, int Total)>(),
                default,
                info.DurationSeconds);

            Assert.Single(Directory.GetFiles(folder, "*.png"));
        }
        finally
        {
            OutputPaths.TryDelete(output);
        }
    }

    private static string? Fixture(string name)
    {
        if (FixtureRoot() is not { } root)
            return null;
        var path = Path.Combine(root, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// TestData resolves <c>..\_local</c> relative to the repository root. In this checkout the worktree
    /// itself lives under <c>_local\wt</c>, so walk the ancestors and use the first shared fixture folder
    /// that exists; DROPWHEEL_TESTDATA and TestData still win when they are set.
    /// </summary>
    private static string? FixtureRoot()
    {
        if (TestData.FixtureRoot is { } configured)
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "_local", "testdata", "TestFiles");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
