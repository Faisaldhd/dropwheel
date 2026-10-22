namespace FileAction.Core.Updates;

/// <summary>
/// The real update checker/downloader: anonymous GitHub lookup, numeric version comparison, a silent
/// background timer (60 s after start, then every 6 h) and a verified, one-at-a-time download.
/// <para>
/// A download is only returned after BOTH trust anchors agree: the SHA-256 against the GitHub asset
/// <c>digest</c> field and against <c>SHA256SUMS.txt</c>, and an ECDSA P-256 signature over the file's
/// SHA-256 that verifies with the key embedded in <see cref="UpdateKey"/>. Anything else deletes the files
/// and reports <see cref="VerificationFailure"/>; the file is never returned or started.
/// </para>
/// </summary>
public sealed class UpdateService : IUpdateService
{
    /// <summary>The one user-facing message a failed verification may show (per the update spec).</summary>
    public const string VerificationFailure = "Update could not be verified";

    private const string CheckFailure = "Could not check for updates";
    private const string DownloadFailure = "Update download failed";

    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _client;
    private readonly string _stagingRoot;
    private readonly Action<string>? _log;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly object _timerLock = new();

    private CancellationTokenSource? _loopCancellation;
    private bool _loopRunning;
    private bool _disposed;
    private Version? _notifiedVersion;

    /// <param name="currentVersion">The running build's version (the assembly version in the app).</param>
    /// <param name="isPortable">True for a folder with <c>portable.txt</c>; selects the zip instead of the setup.</param>
    /// <param name="http">Injectable transport (tests use a fake handler); disposed with the service.</param>
    /// <param name="stagingRoot">Override for <see cref="UpdatePaths.Root"/>; tests point it at a temp folder.</param>
    /// <param name="log">Optional log sink; receives check/download outcomes and never secrets.</param>
    public UpdateService(
        Version currentVersion,
        bool isPortable,
        HttpClient? http = null,
        string? stagingRoot = null,
        Action<string>? log = null)
    {
        CurrentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        IsPortable = isPortable;
        _http = http ?? new HttpClient();
        _client = new GitHubReleaseClient(_http);
        _stagingRoot = string.IsNullOrWhiteSpace(stagingRoot) ? UpdatePaths.Root : stagingRoot;
        _log = log;
    }

    /// <inheritdoc />
    public Version CurrentVersion { get; }

    /// <inheritdoc />
    public bool IsPortable { get; }

    /// <inheritdoc />
    public UpdateInfo? Available { get; private set; }

    /// <inheritdoc />
    public bool IsChecking { get; private set; }

    /// <inheritdoc />
    public bool IsDownloading { get; private set; }

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public event Action<UpdateInfo>? UpdateAvailable;

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <inheritdoc />
    public UpdateAsset? AssetFor(UpdateInfo info) => IsPortable ? info.Portable : info.Setup;

    /// <inheritdoc />
    public async Task<UpdateCheckStatus> CheckAsync(
        bool userInitiated, CancellationToken cancellationToken = default)
    {
        // One check at a time: a "Check now" click during a background check queues behind it instead of
        // racing two state machines. Cancellation before the start propagates, but never mutates state.
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        UpdateCheckStatus status;
        UpdateInfo? found = null;
        var notify = false;

        try
        {
            SetChecking(true);
            LastError = null;

            try
            {
                var info = await _client.GetLatestAsync(cancellationToken).ConfigureAwait(false);

                if (info is null || !IsNewer(info.Version, CurrentVersion))
                {
                    SetAvailable(null);
                    Log(info is null
                        ? "Update check: no stable release is published."
                        : $"Update check: {CurrentVersion} is up to date (latest {info.Version}).");
                    status = UpdateCheckStatus.UpToDate;
                }
                else
                {
                    SetAvailable(info);
                    found = info;
                    if (_notifiedVersion != info.Version)
                    {
                        _notifiedVersion = info.Version;
                        notify = true;
                    }

                    Log($"Update check: {info.Version} is available (tag {info.TagName}).");
                    status = UpdateCheckStatus.Available;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller stopped the check; not an error state.
                throw;
            }
            catch (Exception ex)
            {
                SetAvailable(null);
                LastError = CheckFailure;
                Log($"Update check failed: {Describe(ex)}");
                status = UpdateCheckStatus.Failed;
            }
        }
        finally
        {
            SetChecking(false);
            _checkGate.Release();
        }

        // Award the tray notification at most once per release, and only after the state is consistent.
        if (notify && found is not null)
            UpdateAvailable?.Invoke(found);

        return status;
    }

    /// <inheritdoc />
    public async Task<string?> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (info is null)
        {
            LastError = DownloadFailure;
            return null;
        }

        // Only one download at a time; a second request waits for the first to finish.
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SetDownloading(true);
            LastError = null;

            var asset = AssetFor(info);
            if (asset is null)
            {
                LastError = DownloadFailure;
                Log($"Update download: release {info.TagName} has no {(IsPortable ? "portable" : "setup")} asset.");
                return null;
            }

            var stage = Path.Combine(_stagingRoot, StageName(info.Version));
            var fileName = Path.GetFileName(asset.Name);
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
            {
                LastError = DownloadFailure;
                Log($"Update download: invalid asset name '{asset.Name}'.");
                return null;
            }

            var filePath = Path.Combine(stage, fileName);
            var signaturePath = filePath + ".sig";
            var sumsPath = Path.Combine(stage, GitHubReleaseClient.SumsAssetName);

            try
            {
                Directory.CreateDirectory(stage);
                progress?.Report(0d);
                Log($"Update download: {fileName} ({asset.Size} bytes) from {asset.DownloadUrl}.");
                await DownloadManager
                    .DownloadAsync(_http, asset.DownloadUrl, filePath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteFiles(filePath, signaturePath, sumsPath);
                throw;
            }
            catch (Exception ex)
            {
                DeleteFiles(filePath, signaturePath, sumsPath);
                LastError = DownloadFailure;
                Log($"Update download failed: {Describe(ex)}");
                return null;
            }

            try
            {
                var signatureUrl = asset.SignatureUrl;
                var sumsUrl = info.Sha256SumsUrl;
                if (signatureUrl is null || sumsUrl is null)
                    throw new InvalidDataException("the release is missing its .sig or SHA256SUMS.txt asset");

                await DownloadManager
                    .DownloadAsync(_http, signatureUrl, signaturePath, null, cancellationToken)
                    .ConfigureAwait(false);
                await DownloadManager
                    .DownloadAsync(_http, sumsUrl, sumsPath, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteFiles(filePath, signaturePath, sumsPath);
                throw;
            }
            catch (Exception ex)
            {
                // Without both trust files nothing can be verified, which is the same refusal to the user.
                DeleteFiles(filePath, signaturePath, sumsPath);
                LastError = VerificationFailure;
                Log($"Update verification: trust material unavailable: {Describe(ex)}");
                return null;
            }

            try
            {
                var actual = UpdateVerifier.Sha256Hex(filePath);
                var sumsText = await File.ReadAllTextAsync(sumsPath, cancellationToken).ConfigureAwait(false);
                var signatureBase64 = (await File
                    .ReadAllTextAsync(signaturePath, cancellationToken)
                    .ConfigureAwait(false)).Trim();

                // Both independent digest sources AND the signature must agree. A missing GitHub digest
                // field is a verification failure, not a reason to fall back to the weaker source.
                var digestOk = asset.Sha256Digest is { Length: > 0 } digest &&
                    string.Equals(actual, digest, StringComparison.OrdinalIgnoreCase);
                var sumsOk = Sha256Sums.Matches(sumsText, fileName, actual);
                var signatureOk = UpdateVerifier.VerifySignature(filePath, signatureBase64);

                if (!digestOk || !sumsOk || !signatureOk)
                {
                    DeleteFiles(filePath, signaturePath, sumsPath);
                    LastError = VerificationFailure;
                    Log($"Update verification failed: digest={digestOk}, sums={sumsOk}, signature={signatureOk}.");
                    return null;
                }

                LastError = null;
                progress?.Report(1d);
                Log($"Update download: {fileName} verified.");
                return filePath;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DeleteFiles(filePath, signaturePath, sumsPath);
                throw;
            }
            catch (Exception ex)
            {
                DeleteFiles(filePath, signaturePath, sumsPath);
                LastError = VerificationFailure;
                Log($"Update verification failed: {Describe(ex)}");
                return null;
            }
        }
        finally
        {
            SetDownloading(false);
            _downloadGate.Release();
        }
    }

    /// <inheritdoc />
    public void StartBackgroundChecks()
    {
        lock (_timerLock)
        {
            if (_disposed || _loopRunning)
                return;

            _loopRunning = true;
            _loopCancellation = new CancellationTokenSource();
            var token = _loopCancellation.Token;
            _ = Task.Run(() => BackgroundLoopAsync(token));
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_timerLock)
        {
            cancellation = _loopCancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The loop already finished and disposed its own source.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _http.Dispose();
        // The two gates are deliberately not disposed: a background check may still be unwinding and a
        // Dispose() during shutdown must not turn that into an ObjectDisposedException.
    }

    private async Task BackgroundLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(UpdateConfig.FirstCheckDelay, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Checks only: a background run must never download or apply anything.
                    await CheckAsync(false, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Update check: background check failed: {Describe(ex)}");
                }

                await Task.Delay(UpdateConfig.CheckInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop()/Dispose().
        }
        finally
        {
            lock (_timerLock)
            {
                _loopRunning = false;
                var cancellation = _loopCancellation;
                _loopCancellation = null;
                cancellation?.Dispose();
            }
        }
    }

    /// <summary>
    /// Compares numerically, padding a missing build/revision with 0. Without this,
    /// <c>new Version(2, 2, 0)</c> (the constructor in tests/the app) would look older than the parsed
    /// <c>2.2.0.0</c> and the app would offer itself as an update.
    /// </summary>
    private static bool IsNewer(Version candidate, Version current) =>
        SemanticVersion.IsNewer(Normalize(candidate), Normalize(current));

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>Same name as <see cref="UpdatePaths.StageFor"/> but tolerates a two-part <see cref="Version"/>.</summary>
    private static string StageName(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    private static void DeleteFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the verified-file promise does not depend on the delete succeeding.
            }
        }
    }

    private void SetChecking(bool value)
    {
        if (IsChecking == value)
            return;
        IsChecking = value;
        StateChanged?.Invoke();
    }

    private void SetDownloading(bool value)
    {
        if (IsDownloading == value)
            return;
        IsDownloading = value;
        StateChanged?.Invoke();
    }

    private void SetAvailable(UpdateInfo? value)
    {
        if (ReferenceEquals(Available, value))
            return;
        Available = value;
        StateChanged?.Invoke();
    }

    private void Log(string message) => _log?.Invoke(message);
}
