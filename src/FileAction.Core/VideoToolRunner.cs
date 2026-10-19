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
    /// <summary>Trims, copying packets when asked and re-encoding when a codec/container pair refuses a copy.</summary>
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
            try
            {
                await ProcessRunner.RunAsync(
                    ffmpeg, VideoEdit.BuildTrimArgs(withOutput), cancellationToken).ConfigureAwait(false);
            }
            catch (ConversionException) when (request.Mode == VideoTrimMode.StreamCopy && !cancellationToken.IsCancellationRequested)
            {
                await ProcessRunner.RunAsync(
                    ffmpeg,
                    VideoEdit.BuildTrimArgs(withOutput with { Mode = VideoTrimMode.ReEncode }),
                    cancellationToken).ConfigureAwait(false);
            }
        });
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
        });
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
                cancellationToken));
    }

    /// <summary>
    /// A <see cref="OutputPaths.ProduceAsync"/> variant whose cleanup retries while a just-killed FFmpeg
    /// still holds the temporary file; the failure tests assert that no <c>.part</c> file survives.
    /// </summary>
    private static async Task<string> ProduceAsync(
        string directory,
        string baseName,
        string extension,
        Func<string, Task> produce)
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
            await DeleteWhenFreeAsync(temporary).ConfigureAwait(false);
        }
    }

    private static async Task DeleteWhenFreeAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (!File.Exists(path))
                    return;
                File.Delete(path);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
        }
    }
}
