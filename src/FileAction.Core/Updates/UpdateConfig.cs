namespace FileAction.Core.Updates;

/// <summary>One place for the update endpoint and asset naming (the spec calls for a single repository constant).</summary>
public static class UpdateConfig
{
    /// <summary>The only repository the app checks. Public; the request is anonymous.</summary>
    public const string Repository = "Faisaldhd/dropwheel";

    /// <summary>User-Agent GitHub requires for anonymous API calls.</summary>
    public const string UserAgent = "DropWheel";

    /// <summary>Latest stable release endpoint.</summary>
    public const string LatestReleaseUrl = "https://api.github.com/repos/Faisaldhd/dropwheel/releases/latest";

    /// <summary>Release page pattern used for "What's new".</summary>
    public const string ReleasePageFormat = "https://github.com/Faisaldhd/dropwheel/releases/tag/{0}";

    /// <summary>Portable download page used by the website (stable asset name).</summary>
    public const string StableSetupAssetName = "DropWheel-Setup.exe";

    /// <summary>Setup asset for a version: <c>DropWheel-2.2.0-Setup.exe</c>.</summary>
    public static string SetupAssetName(Version version) => $"DropWheel-{version.ToString(3)}-Setup.exe";

    /// <summary>Portable zip asset for a version: <c>DropWheel-2.2.0-portable-win-x64.zip</c>.</summary>
    public static string PortableAssetName(Version version) => $"DropWheel-{version.ToString(3)}-portable-win-x64.zip";

    /// <summary>First check runs one minute after start.</summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(60);

    /// <summary>Then every six hours; the manual Check for updates button remains immediate.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>Network timeout; a slow GitHub must never block the UI.</summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
}
