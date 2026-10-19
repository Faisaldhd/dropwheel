using System.IO;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Video;

/// <summary>
/// Runs a split plan. Each part is attempted as a verified stream copy first: the copied file's length
/// is measured against the requested range, and a part that came out longer (a copy can only start at
/// a keyframe, which MKV/WebM cannot hide) is deleted and re-encoded instead. A failed copy (codecs the
/// container does not accept) takes the same re-encode path. The part folder belongs to this run, so a
/// cancellation or failure removes it rather than leaving a half-written set behind.
/// </summary>
internal static class VideoSplitRunner
{
    public static async Task<string> RunAsync(
        string ffmpeg, SplitPlan plan, IProgress<double> progress, CancellationToken ct, Settings? settings = null)
    {
        var directory = OutputTarget.SourceFolder(plan.SourcePath) ?? Environment.CurrentDirectory;
        var folder = OutputPaths.UniqueDirectory(directory, plan.FolderName);
        Directory.CreateDirectory(folder);
        var quality = settings ?? new Settings();

        try
        {
            for (var index = 0; index < plan.Segments.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var number = index + 1;
                var name = VideoSplitJoin.SegmentName(plan.BaseName, number, plan.Segments.Count);
                await ProduceAsync(ffmpeg, plan, plan.Segments[index], folder, name, quality, ct).ConfigureAwait(false);
                progress.Report((double)number / plan.Segments.Count);
            }
            return folder;
        }
        catch
        {
            OutputPaths.TryDelete(folder);
            throw;
        }
    }

    private static async Task<string> ProduceAsync(
        string ffmpeg, SplitPlan plan, SplitSegment segment, string folder, string name, Settings settings, CancellationToken ct)
    {
        if (plan.CanCopy)
        {
            try
            {
                var copied = await WriteAsync(ffmpeg, plan, segment, folder, name, copy: true, settings, ct).ConfigureAwait(false);
                if (await LengthMatchesAsync(ffmpeg, copied, segment, ct).ConfigureAwait(false))
                    return copied;

                // The copy started at an earlier keyframe and kept that extra content: re-encode the range.
                OutputPaths.TryDelete(copied);
            }
            catch (ConversionException) when (!ct.IsCancellationRequested)
            {
                // The container refused the copied codecs; the re-encode below writes a playable part.
            }
        }

        return await WriteAsync(ffmpeg, plan, segment, folder, name, copy: false, settings, ct).ConfigureAwait(false);
    }

    private static Task<string> WriteAsync(
        string ffmpeg,
        SplitPlan plan,
        SplitSegment segment,
        string folder,
        string name,
        bool copy,
        Settings settings,
        CancellationToken ct) =>
        OutputPaths.ProduceAsync(folder, name, plan.Extension, temp =>
            ProcessRunner.RunAsync(
                ffmpeg,
                VideoSplitJoin.SplitArgs(plan.SourcePath, segment, temp, copy, plan.Extension, settings),
                ct));

    private static async Task<bool> LengthMatchesAsync(string ffmpeg, string part, SplitSegment segment, CancellationToken ct)
    {
        var info = await VideoToolCommon.ProbeAsync(ffmpeg, part, ct).ConfigureAwait(false);
        return VideoSplitJoin.CopyLengthMatches(segment.Duration, info.Duration);
    }
}

/// <summary>
/// Runs a join: one ffmpeg call with the concat filter, so every clip is scaled and padded to the first
/// clip's frame and a silent clip gets silence of its own length. Progress follows ffmpeg's time= lines
/// against the total length of the clips.
/// </summary>
internal static class VideoJoinRunner
{
    public static async Task<string> RunAsync(
        string ffmpeg,
        IReadOnlyList<ClipInfo> clips,
        IProgress<double> progress,
        CancellationToken ct,
        Settings? settings = null)
    {
        if (clips.Count < 2)
            throw new ConversionException(T("tool.videojoin.noClips"));

        var directory = OutputTarget.SourceFolder(clips[0].Path) ?? Environment.CurrentDirectory;
        var total = clips.Sum(clip => clip.Duration);
        var reported = 0d;
        var quality = settings ?? new Settings();

        return await OutputPaths.ProduceAsync(
            directory,
            VideoSplitJoin.JoinedName,
            VideoSplitJoin.JoinedExtension,
            async temp =>
            {
                var args = VideoSplitJoin.JoinArgs(clips, temp, quality);
                await ProcessRunner.RunAsync(ffmpeg, args, ct, line =>
                {
                    if (total <= 0 || VideoToolCommon.ProgressSeconds(line) is not { } done)
                        return;
                    var value = Math.Clamp(done / total, 0, 0.99);
                    if (value <= reported)
                        return;
                    reported = value;
                    progress.Report(value);
                }).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}
