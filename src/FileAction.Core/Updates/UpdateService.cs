namespace FileAction.Core.Updates;

/// <summary>
/// Stub of the real updater; the update-core agent replaces this file with the GitHub client, asset selection,
/// download manager and verification flow. The app and the tests compile against <see cref="IUpdateService"/>.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    private readonly string _stagingRoot;

    public UpdateService(Version currentVersion, bool isPortable, HttpClient? http = null, string? stagingRoot = null)
    {
        CurrentVersion = currentVersion;
        IsPortable = isPortable;
        _http = http ?? new HttpClient();
        _stagingRoot = stagingRoot ?? UpdatePaths.Root;
    }

    public Version CurrentVersion { get; }

    public bool IsPortable { get; }

    public UpdateInfo? Available { get; private set; }

    public bool IsChecking { get; private set; }

    public bool IsDownloading { get; private set; }

    public string? LastError { get; private set; }

    public event Action<UpdateInfo>? UpdateAvailable;

    public event Action? StateChanged;

    public UpdateAsset? AssetFor(UpdateInfo info) => IsPortable ? info.Portable : info.Setup;

    public Task<UpdateCheckStatus> CheckAsync(bool userInitiated, CancellationToken cancellationToken = default) =>
        Task.FromResult(UpdateCheckStatus.UpToDate);

    public Task<string?> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public void StartBackgroundChecks()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
