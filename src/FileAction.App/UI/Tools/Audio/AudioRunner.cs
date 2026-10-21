using System.IO;
using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>Peak envelope and exact duration decoded from the same waveform pass.</summary>
internal sealed record AudioPeaks(float[] Peaks, double Duration);

/// <summary>
/// Runs the FFmpeg work the audio tools need (loudness measurement, silence detection, waveform decode,
/// and the two writes) and keeps the file-system rules in one place. Everything runs off the UI thread;
/// the caller passes a token so closing the tool cancels the process tree.
/// </summary>
internal static class AudioRunner
{
    /// <summary>Envelope resolution kept independent of the window width, so resizing never re-decodes.</summary>
    public const int WaveformBuckets = 1500;

    public static string RequireFfmpeg(ToolLocator tools) => tools.Require(ExternalTool.Ffmpeg);

    /// <summary>
    /// Reads the first audio stream's sample rate and channel count from FFmpeg's stream banner. The
    /// command has no output and exits non-zero by design, so its log is read but no failure is raised;
    /// null means FFmpeg found no readable audio stream.
    /// </summary>
    public static async Task<AudioProbeInfo?> ProbeAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var log = new StringBuilder();
        await ProcessRunner.RunAsync(
            ffmpeg,
            ["-hide_banner", "-nostdin", "-i", path],
            ct,
            line =>
            {
                lock (log)
                    log.AppendLine(line);
            },
            throwOnError: false).ConfigureAwait(false);
        return AudioProbe.Parse(log.ToString());
    }

    /// <summary>The first loudness pass; null when FFmpeg exited successfully but printed no usable JSON.</summary>
    public static async Task<LoudnessMeasurement?> MeasureAsync(
        string ffmpeg,
        string path,
        LoudnessTarget target,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        await RunProcessAsync(
            ffmpeg,
            AudioLoudness.MeasureArguments(path, target),
            ct,
            line =>
            {
                lock (log)
                    log.AppendLine(line);
            }).ConfigureAwait(false);
        return AudioLoudness.Parse(log.ToString());
    }

    /// <summary>
    /// Decodes the audio to mono PCM in a temp file and reduces it to the peak envelope. The PCM is read in
    /// chunks, so a long recording never has to fit in memory; the temp file is removed either way.
    /// </summary>
    public static async Task<AudioPeaks> WaveformAsync(string ffmpeg, string path, CancellationToken ct)
    {
        var directory = OutputPaths.TempDirectory();
        try
        {
            var pcm = Path.Combine(directory, "waveform.pcm");
            await RunProcessAsync(ffmpeg, AudioTrim.WaveformArguments(path, pcm), ct).ConfigureAwait(false);

            var length = new FileInfo(pcm).Length;
            var duration = AudioTrim.PcmDuration(length);
            var totalSamples = length / 2;
            var peaks = new float[WaveformBuckets];
            if (totalSamples > 0)
            {
                var buffer = new byte[64 * 1024];
                var firstSample = 0L;
                await using var stream = new FileStream(
                    pcm, FileMode.Open, FileAccess.Read, FileShare.Read,
                    buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
                int read;
                while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    var usable = read - read % 2;
                    AudioTrim.AccumulatePeaks(buffer.AsSpan(0, usable), firstSample, totalSamples, peaks);
                    firstSample += usable / 2;
                }
            }
            return new AudioPeaks(peaks, duration);
        }
        finally
        {
            OutputPaths.TryDelete(directory);
        }
    }

    /// <summary>The leading/trailing silence spans ffmpeg reported for the file.</summary>
    public static async Task<IReadOnlyList<SilenceSpan>> DetectSilenceAsync(
        string ffmpeg,
        string path,
        CancellationToken ct)
    {
        var log = new StringBuilder();
        await RunProcessAsync(
            ffmpeg,
            AudioTrim.DetectSilenceArguments(path),
            ct,
            line =>
            {
                lock (log)
                    log.AppendLine(line);
            }).ConfigureAwait(false);
        return AudioTrim.ParseSilence(log.ToString());
    }

    /// <summary>
    /// Second loudness pass into a unique "&lt;name&gt; Normalized" file next to the source. The source
    /// sample rate is passed through so the output is not left at loudnorm's internal 192 kHz.
    /// </summary>
    public static Task<string> NormalizeAsync(
        string ffmpeg,
        FileItem item,
        LoudnessTarget target,
        LoudnessMeasurement measurement,
        string outputDirectory,
        int sampleRate,
        CancellationToken ct)
    {
        var format = FfmpegBackend.SafeContainer(item.Format);
        var encoder = FfmpegBackend.CodecArgs(format, new Settings());
        return OutputPaths.ProduceAsync(
            outputDirectory,
            $"{item.BaseName} {T("tool.audionormalize.suffix")}",
            format,
            tmp => RunProcessAsync(
                ffmpeg,
                AudioLoudness.ApplyArguments(item.Path, tmp, target, measurement, encoder, sampleRate),
                ct));
    }

    /// <summary>Writes only the selection to a unique "&lt;name&gt; Trimmed" file next to the source.</summary>
    public static Task<string> TrimAsync(
        string ffmpeg,
        FileItem item,
        TrimRange range,
        string outputDirectory,
        CancellationToken ct)
    {
        var format = FfmpegBackend.SafeContainer(item.Format);
        var encoder = FfmpegBackend.CodecArgs(format, new Settings());
        return OutputPaths.ProduceAsync(
            outputDirectory,
            $"{item.BaseName} {T("tool.audiotrim.suffix")}",
            format,
            tmp => RunProcessAsync(ffmpeg, AudioTrim.TrimArguments(item.Path, tmp, range, encoder), ct));
    }

    /// <summary>
    /// The folder a tool writes into: the drop target when it is usable (same rules as every other tool),
    /// otherwise next to the source. The source file itself is never touched.
    /// </summary>
    public static string OutputDirectory(FileItem item, DropTargets? targets)
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

    /// <summary>
    /// A message for the status line: the known "no audio stream" failures get a translated sentence,
    /// everything else keeps FFmpeg's own text (which also names the missing tool when that is the cause).
    /// </summary>
    public static string Message(Exception exception, string noAudioKey)
    {
        var text = exception.Message;
        return text.Contains("matches no streams", StringComparison.OrdinalIgnoreCase)
            || text.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
            ? T(noAudioKey)
            : text;
    }

    /// <summary>
    /// Runs one FFmpeg command. On cancellation the process tree is already killed by
    /// <see cref="ProcessRunner"/>; the short pause lets Windows release the output file before
    /// <see cref="OutputPaths.ProduceAsync"/> deletes its temporary file, so a cancelled run leaves nothing.
    /// </summary>
    private static async Task RunProcessAsync(
        string ffmpeg,
        IEnumerable<string> arguments,
        CancellationToken ct,
        Action<string>? onStdErr = null)
    {
        try
        {
            await ProcessRunner.RunAsync(ffmpeg, arguments, ct, onStdErr).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Task.Delay(400, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}
