using System.IO;

namespace FileAction.Core.Updates;

/// <summary>Outcome of one update check. <see cref="Failed"/> is silent in the UI unless it was user-initiated.</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

/// <summary>One downloadable release asset and the trust material that comes with it.</summary>
public sealed record UpdateAsset(
    string Name,
    Uri DownloadUrl,
    long Size,
    string? Sha256Digest,
    Uri? SignatureUrl);

/// <summary>The parts of the latest GitHub release the updater needs.</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    Uri ReleasePage,
    UpdateAsset? Setup,
    UpdateAsset? Portable,
    Uri? Sha256SumsUrl);

/// <summary>
/// The update checker used by the app. Implemented by <see cref="UpdateService"/>; the UI only depends on
/// this interface so the network and signature rules stay testable.
/// </summary>
public interface IUpdateService : IDisposable
{
    /// <summary>The running build's version, read from the assembly.</summary>
    Version CurrentVersion { get; }

    /// <summary>True when the app runs from a folder with <c>portable.txt</c>; selects the zip instead of the setup.</summary>
    bool IsPortable { get; }

    /// <summary>The newer release, or null when the app is current / the last check failed.</summary>
    UpdateInfo? Available { get; }

    bool IsChecking { get; }

    bool IsDownloading { get; }

    /// <summary>Last user-facing error, or null. Background failures stay silent and only log.</summary>
    string? LastError { get; }

    /// <summary>Raised once when a newer release is found (background check or on demand).</summary>
    event Action<UpdateInfo>? UpdateAvailable;

    /// <summary>Raised whenever <see cref="Available"/>, <see cref="IsChecking"/> or <see cref="IsDownloading"/> change.</summary>
    event Action? StateChanged;

    /// <summary>The asset this install kind must download: the setup exe, or the portable zip.</summary>
    UpdateAsset? AssetFor(UpdateInfo info);

    /// <summary>
    /// Checks the latest release. <paramref name="userInitiated"/> only decides whether an error is surfaced;
    /// network failures never throw. Background checks run 60 s after start and every 24 h.
    /// </summary>
    Task<UpdateCheckStatus> CheckAsync(bool userInitiated, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the selected asset, its <c>.sig</c> and <c>SHA256SUMS.txt</c>, verifies the SHA-256 digest and the
    /// ECDSA P-256 signature, and returns the verified file path. Returns null (and deletes the files) on any
    /// mismatch; only one download may run at a time.
    /// </summary>
    Task<string?> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Starts the silent 60 s / 24 h timer loop. Calling it twice is harmless.</summary>
    void StartBackgroundChecks();

    /// <summary>Stops the timer loop (app exit, or tests).</summary>
    void Stop();
}

/// <summary>Paths the update flow uses. Kept in one place so the app and the tests agree.</summary>
public static class UpdatePaths
{
    /// <summary>True when a <c>portable.txt</c> marker sits next to the exe.</summary>
    public static bool IsPortable => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    /// <summary><c>%LOCALAPPDATA%\DropWheel\updates</c>; never inside the install folder.</summary>
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel", "updates");

    /// <summary>Staging folder for one version.</summary>
    public static string StageFor(Version version) => Path.Combine(Root, version.ToString(3));
}
