namespace FileAction.Core.Updates;

/// <summary>
/// Streams one release asset to disk and reports progress from the response <c>Content-Length</c>, so the
/// updater never holds a whole zip in memory. Callers own verification and error handling.
/// </summary>
public static class DownloadManager
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Downloads <paramref name="url"/> into <paramref name="destinationPath"/> (creating the folder and
    /// replacing any previous file). Progress is a 0..1 fraction when the server sends a content length and
    /// jumps to 1 when the last byte arrives.
    /// </summary>
    public static async Task DownloadAsync(
        HttpClient http,
        Uri url,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long received = 0;
        while (true)
        {
            var read = await input
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            await output
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            received += read;

            if (total is > 0)
                progress?.Report(Math.Clamp((double)received / total.Value, 0d, 1d));
        }

        progress?.Report(1d);
    }
}
