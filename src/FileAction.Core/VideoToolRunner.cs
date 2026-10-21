namespace FileAction.Core;

/// <summary>
/// Runs a video edit end to end without any window: builds the FFmpeg arguments from a request, writes a
/// temporary file next to the result, moves it to a unique final name (nothing is overwritten) and applies
/// the fallbacks the tools advertise. The three views call exactly these methods, so the real-FFmpeg
/// function tests exercise the shipped code path. Temporary output is deleted with a short retry so a
/// cancelled FFmpeg process that is still closing its file cannot leave a partial file behind.
/// </summary>
public static class VideoToolRunner
{
    /// <summary>
    /// Trims, copying packets when asked and re-encoding when a codec/container pair refuses a copy or
    /// when the copied file's measured length shows it began at an earlier keyframe.
    /// </summary>
    public static Task<string> TrimAsync(
        string ffmpeg,
        VideoTrimRequest request,
        string outputDirectory,
        string baseName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ffmpeg);
        ArgumentNullException.ThrowIfNull(request);

        var container = VideoEdit.OutputContainerFor(request.Container);
        return ProduceAsync(outputDirectory, baseName, container, async temporary =>
        {
            var withOutput = request with { OutputPath = temporary };
            if (request.Mode == VideoTrimMode.StreamCopy && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessRunner.RunAsync(
                        ffmpeg, VideoEdit.BuildTrimArgs(withOutput), cancellationToken).ConfigureAwait(false);

                    // Split verifies every copied part's length for the same reason: a copy can only
                    // begin at a keyframe, and a container without an edit list keeps the pre-roll.
                    // If the measured length is wrong, encode the requested range instead.
                    if (await CopyTrimMatchesAsync(ffmpeg, temporary, request, cancellationToken).ConfigureAwait(false))
                        return;
                }
                catch (ConversionException) when (!cancellationToken.IsCancellationRequested)
                {
                    // The container refused the copied codecs; the re-encode below writes a playable file.
                }
            }

            await ProcessRunner.RunAsync(
                ffmpeg,
                VideoEdit.BuildTrimArgs(withOutput with { Mode = VideoTrimMode.ReEncode }),
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Crops with even pixel values and keeps the audio; retries with encoded audio if the container refuses a copy.</summary>
    public static Task<string> CropAsync(
        string ffmpeg,
        VideoCropRequest request,
        string outputDirectory,
        string baseName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ffmpeg);
        ArgumentNullException.ThrowIfNull(request);

        var container = VideoEdit.OutputContainerFor(request.Container);
        return ProduceAsync(outputDirectory, baseName, container, async temporary =>
        {
            var withOutput = request with { OutputPath = temporary };
            try
            {
                await ProcessRunner.RunAsync(
                    ffmpeg, VideoEdit.BuildCropArgs(withOutput), cancellationToken).ConfigureAwait(false);
            }
            catch (ConversionException) when (request.HasAudio && request.CopyAudio && !cancellationToken.IsCancellationRequested)
            {
                await ProcessRunner.RunAsync(
                    ffmpeg,
                    VideoEdit.BuildCropArgs(withOutput with { CopyAudio = false }),
                    cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <summary>Changes the speed with a setpts/atempo pair; a silent clip simply has no audio filter.</summary>
    public static Task<string> SpeedAsync(
        string ffmpeg,
        VideoSpeedRequest request,
        string outputDirectory,
        string baseName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ffmpeg);
        ArgumentNullException.ThrowIfNull(request);

        var container = VideoEdit.OutputContainerFor(request.Container);
        return ProduceAsync(outputDirectory, baseName, container, temporary =>
            ProcessRunner.RunAsync(
                ffmpeg,
                VideoEdit.BuildSpeedArgs(request with { OutputPath = temporary }),
                cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Reads a copied trim's real length and compares it with the requested range. A null probe means
    /// FFmpeg cannot read its own output, which is just as unusable as a wrong length.
    /// </summary>
    private static async Task<bool> CopyTrimMatchesAsync(
        string ffmpeg,
        string path,
        VideoTrimRequest request,
        CancellationToken cancellationToken)
    {
        var expected = request.End - (request.Start < TimeSpan.Zero ? TimeSpan.Zero : request.Start);
        var info = await VideoProbe.ReadAsync(ffmpeg, path, cancellationToken).ConfigureAwait(false);
        return info is not null && VideoEdit.CopyLengthMatches(expected, info.Duration);
    }

    /// <summary>
    /// A <see cref="OutputPaths.ProduceAsync"/> variant whose cleanup retries while a just-killed FFmpeg
    /// still holds the temporary file; the failure tests assert that no <c>.part</c> file survives.
    /// </summary>
    private static async Task<string> ProduceAsync(
        string directory,
        string baseName,
        string extension,
        Func<string, Task> produce,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{baseName}.{Guid.NewGuid():N}.part.{extension}");
        try
        {
            await produce(temporary).ConfigureAwait(false);
            if (!File.Exists(temporary))
                throw new ConversionException(Loc.T("error.conversion.noOutput"));

            var final = OutputPaths.Unique(directory, baseName, extension);
            File.Move(temporary, final);
            return final;
        }
        finally
        {
            await DeleteWhenFreeAsync(temporary, cancellationToken.IsCancellationRequested).ConfigureAwait(false);
        }
    }

    private static async Task DeleteWhenFreeAsync(string path, bool waitForLateFile)
    {
        // Process.Kill is asynchronous. After a cancellation the dying FFmpeg can still be closing its
        // file (or create it a moment after the kill), so keep watching for a second; on every other
        // path the file is either finished or was never created.
        var attempts = waitForLateFile ? 20 : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return;
                }
                if (!waitForLateFile)
                    return;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (attempt + 1 < attempts)
                await Task.Delay(50).ConfigureAwait(false);
        }
    }
}
