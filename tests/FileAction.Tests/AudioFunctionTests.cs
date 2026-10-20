using System.Globalization;
using FileAction.App.UI.Tools.Audio;
using FileAction.Core;
using Xunit;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// Drives the real <see cref="AudioRunner"/> FFmpeg paths (not hand-built command lines) on audio generated
/// by FFmpeg and checks the files they write, including the failure and cancellation paths. Skipped when
/// FFmpeg is not installed; CI installs it before the test step.
/// </summary>
[Trait("Category", "Integration")]
public class AudioFunctionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir = OutputPaths.TempDirectory();
    private readonly string? _ffmpeg = new ToolLocator().Ffmpeg;

    public AudioFunctionTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => OutputPaths.TryDelete(_dir);

    private bool SkipWithoutFfmpeg()
    {
        if (_ffmpeg is not null)
            return false;
        _output.WriteLine("skipped: ffmpeg is not installed");
        return true;
    }

    /// <summary>0.5 s silence, a −20 dBFS 440 Hz tone, 0.5 s silence (four seconds in total).</summary>
    private async Task<string> MakeToneAsync(string name = "tone.wav", double toneSeconds = 3)
    {
        var path = Path.Combine(_dir, name);
        var tone = toneSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        await ProcessRunner.RunAsync(
            _ffmpeg!,
            [
                "-hide_banner", "-nostdin", "-nostats", "-y",
                "-f", "lavfi",
                "-i", $"aevalsrc=0.1*sin(2*PI*440*t)|0.1*sin(2*PI*440*t):d={tone}",
                "-af", "adelay=500:all=1,apad=pad_dur=0.5",
                "-ac", "2",
                "-c:a", "pcm_s16le",
                path,
            ],
            CancellationToken.None).ConfigureAwait(false);
        return path;
    }

    private static string OutputFolder(string parent, string name)
    {
        var folder = Path.Combine(parent, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public async Task Normalize_reaches_the_requested_loudness_and_true_peak()
    {
        if (SkipWithoutFfmpeg())
            return;

        var source = await MakeToneAsync("normalize-source.wav");
        var target = LoudnessTarget.Default;
        var before = await AudioRunner.MeasureAsync(_ffmpeg!, source, target, CancellationToken.None);
        Assert.NotNull(before);

        var outDir = OutputFolder(_dir, "normalize-out");
        var created = await AudioRunner.NormalizeAsync(
            _ffmpeg!, new FileItem(source), target, before!, outDir, CancellationToken.None);

        Assert.True(File.Exists(created), created);
        Assert.True(new FileInfo(created).Length > 0, "empty output");
        Assert.Empty(Directory.GetFiles(outDir, "*.part.*"));

        var after = await AudioRunner.MeasureAsync(_ffmpeg!, created, target, CancellationToken.None);
        Assert.NotNull(after);
        Assert.InRange(after!.Integrated, target.IntegratedLufs - 1.0, target.IntegratedLufs + 1.0);
        Assert.True(
            after.TruePeak <= target.TruePeakDb + 0.1,
            $"true peak {after.TruePeak:0.00} dBTP is over the {target.TruePeakDb:0.0} dBTP limit");
        _output.WriteLine(
            $"normalize: {target.IntegratedLufs:0.0} LUFS target, got {after.Integrated:0.00} LUFS / {after.TruePeak:0.00} dBTP");
    }

    [Fact]
    public async Task Trim_writes_the_selected_duration()
    {
        if (SkipWithoutFfmpeg())
            return;

        var source = await MakeToneAsync("trim-manual.wav");
        var outDir = OutputFolder(_dir, "trim-manual-out");

        var created = await AudioRunner.TrimAsync(
            _ffmpeg!, new FileItem(source), new TrimRange(1.0, 2.0), outDir, CancellationToken.None);

        Assert.True(File.Exists(created), created);
        Assert.Empty(Directory.GetFiles(outDir, "*.part.*"));

        var decoded = await AudioRunner.WaveformAsync(_ffmpeg!, created, CancellationToken.None);
        Assert.InRange(decoded.Duration, 2.0 - 0.010, 2.0 + 0.010);
        _output.WriteLine($"trim: selected 2.000 s, output {decoded.Duration:0.000} s");
    }

    [Fact]
    public async Task Trim_silence_removes_only_the_silent_ends()
    {
        if (SkipWithoutFfmpeg())
            return;

        var source = await MakeToneAsync("trim-silence.wav");
        var sourceAudio = await AudioRunner.WaveformAsync(_ffmpeg!, source, CancellationToken.None);
        Assert.InRange(sourceAudio.Duration, 4.0 - 0.010, 4.0 + 0.010);

        var spans = await AudioRunner.DetectSilenceAsync(_ffmpeg!, source, CancellationToken.None);
        Assert.True(spans.Count >= 2, $"only {spans.Count} silence span(s)");
        var range = AudioTrim.TrimSilence(spans, sourceAudio.Duration);

        // 0.5 s of silence at each edge, keeping 50 ms of padding: the tone remains, only the edges go.
        Assert.InRange(range.Start, 0.40, 0.50);
        Assert.InRange(range.End, 3.50, 3.60);

        var outDir = OutputFolder(_dir, "trim-silence-out");
        var created = await AudioRunner.TrimAsync(_ffmpeg!, new FileItem(source), range, outDir, CancellationToken.None);
        var decoded = await AudioRunner.WaveformAsync(_ffmpeg!, created, CancellationToken.None);
        Assert.InRange(decoded.Duration, range.Duration - 0.010, range.Duration + 0.010);

        // The output is the audible middle, not the whole file and not silence.
        Assert.InRange(decoded.Duration, 2.9, 3.3);
        var before = await AudioRunner.MeasureAsync(_ffmpeg!, source, LoudnessTarget.Default, CancellationToken.None);
        var after = await AudioRunner.MeasureAsync(_ffmpeg!, created, LoudnessTarget.Default, CancellationToken.None);
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(after!.Integrated > -23, $"output is still silent: {after.Integrated:0.00} LUFS");
        Assert.True(
            after.Integrated > before!.Integrated + 0.2,
            $"output loudness {after.Integrated:0.00} vs source {before.Integrated:0.00}");
        Assert.Empty(Directory.GetFiles(outDir, "*.part.*"));
        _output.WriteLine(
            $"silence trim: kept {range.Start:0.000}–{range.End:0.000} s, output {decoded.Duration:0.000} s");
    }

    [Fact]
    public async Task Corrupt_input_fails_cleanly_without_output_files()
    {
        if (SkipWithoutFfmpeg())
            return;

        var corrupt = Path.Combine(_dir, "corrupt.wav");
        await File.WriteAllBytesAsync(
            corrupt,
            Enumerable.Range(0, 2048).Select(index => (byte)(index * 37)).ToArray());
        var outDir = OutputFolder(_dir, "corrupt-out");

        var measureError = await Assert.ThrowsAnyAsync<ConversionException>(
            () => AudioRunner.MeasureAsync(_ffmpeg!, corrupt, LoudnessTarget.Default, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(measureError.Message));

        var trimError = await Assert.ThrowsAnyAsync<ConversionException>(
            () => AudioRunner.TrimAsync(
                _ffmpeg!, new FileItem(corrupt), new TrimRange(0, 1), outDir, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(trimError.Message));
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Missing_ffmpeg_reports_a_clear_error_and_writes_nothing()
    {
        if (SkipWithoutFfmpeg())
            return;

        var source = await MakeToneAsync("missing-ffmpeg.wav");
        var missing = Path.Combine(_dir, "ffmpeg-not-here.exe");
        var outDir = OutputFolder(_dir, "missing-ffmpeg-out");

        var trimError = await Assert.ThrowsAnyAsync<ConversionException>(
            () => AudioRunner.TrimAsync(
                missing, new FileItem(source), new TrimRange(0, 1), outDir, CancellationToken.None));
        Assert.Contains("ffmpeg-not-here.exe", trimError.Message, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAnyAsync<ConversionException>(
            () => AudioRunner.MeasureAsync(missing, source, LoudnessTarget.Default, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Windows does not stop writes to a folder whose read-only attribute is set (and CI runs elevated), so
    /// this uses a path whose parent is a file: creating the output folder must fail, and nothing is written.
    /// </summary>
    [Fact]
    public async Task An_output_folder_that_cannot_be_created_fails_cleanly()
    {
        if (SkipWithoutFfmpeg())
            return;

        var source = await MakeToneAsync("blocked-source.wav");
        var blocker = Path.Combine(_dir, "blocked");
        await File.WriteAllTextAsync(blocker, "not a folder");
        var outDir = Path.Combine(blocker, "out");

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => AudioRunner.TrimAsync(
                _ffmpeg!, new FileItem(source), new TrimRange(0, 1), outDir, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(Directory.Exists(outDir));
    }

    [Fact]
    public async Task Cancel_mid_run_leaves_no_partial_output()
    {
        if (SkipWithoutFfmpeg())
            return;

        // A long file keeps the trim pass busy long enough to cancel it while FFmpeg is writing.
        var source = await MakeToneAsync("cancel-source.wav", toneSeconds: 240);
        var outDir = OutputFolder(_dir, "cancel-out");

        using var cts = new CancellationTokenSource();
        var task = AudioRunner.TrimAsync(
            _ffmpeg!, new FileItem(source), new TrimRange(0, 240), outDir, cts.Token);

        // Cancel once FFmpeg has created its hidden temporary file, i.e. the run is genuinely in flight.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline && !task.IsCompleted
               && Directory.GetFiles(outDir, "*.part.*").Length == 0)
        {
            await Task.Delay(10);
        }
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(Directory.GetFiles(outDir, "*", SearchOption.AllDirectories));
    }
}
