using System.Globalization;
using System.Text.Json;

namespace FileAction.Core.Updates;

/// <summary>
/// The anonymous GitHub release API client. One GET of <see cref="UpdateConfig.LatestReleaseUrl"/> with the
/// headers GitHub requires, a 10 s deadline, and a <see cref="JsonDocument"/>-based parse (no reflection), so
/// the trimmed app keeps working. No token is ever sent: the repository is public.
/// </summary>
public sealed class GitHubReleaseClient
{
    /// <summary>The release asset that lists the SHA-256 of the downloadable binaries.</summary>
    public const string SumsAssetName = "SHA256SUMS.txt";

    /// <summary>Pinned API version; a GitHub-side default change must not alter the payload shape silently.</summary>
    public const string ApiVersion = "2022-11-28";

    private const string AcceptMediaType = "application/vnd.github+json";

    private readonly HttpClient _http;

    public GitHubReleaseClient(HttpClient http) =>
        _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>
    /// Fetches the latest release. Returns null when it is a draft, a prerelease, or carries a prerelease tag
    /// (nothing to offer). Transport, HTTP and malformed-payload failures throw, so the caller maps them to a
    /// failed check instead of silently claiming "up to date".
    /// </summary>
    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(UpdateConfig.HttpTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateConfig.LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd(UpdateConfig.UserAgent);
        request.Headers.Accept.ParseAdd(AcceptMediaType);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return ParseLatest(json);
    }

    /// <summary>
    /// Parses one <c>/releases/latest</c> payload. Drafts and prereleases return null; a payload the updater
    /// cannot use (no tag, junk tag) throws <see cref="JsonException"/>.
    /// </summary>
    public static UpdateInfo? ParseLatest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("The release payload is empty.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("The release payload is not a JSON object.");

        if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
            return null;

        var tag = GetString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
            throw new JsonException("The release payload has no tag_name.");

        var tagName = tag.Trim();
        if (!SemanticVersion.TryParse(tagName, out var parsed))
        {
            // A prerelease tag (2.3.0-beta.1) is expected on the endpoint now and then: ignore it.
            // Anything else is a malformed release and must surface as a failed check, not "up to date".
            if (tagName.IndexOf('-') >= 0)
                return null;
            throw new JsonException($"The release tag '{tagName}' is not a version.");
        }

        // SemanticVersion always returns four parts. A zero revision is the normal, user-visible
        // "2.3.0", so store the shortest exact form (Version.ToString()/the UI must not print 2.3.0.0).
        var version = parsed.Revision > 0
            ? parsed
            : new Version(parsed.Major, parsed.Minor, parsed.Build);

        var releasePage = GetUri(root, "html_url") ??
            new Uri(string.Format(CultureInfo.InvariantCulture, UpdateConfig.ReleasePageFormat, tagName));

        var assets = ReadAssets(root);
        var setup = BuildAsset(UpdateConfig.SetupAssetName(version), assets);
        var portable = BuildAsset(UpdateConfig.PortableAssetName(version), assets);
        var sums = assets.TryGetValue(SumsAssetName, out var sumsAsset) ? sumsAsset.Url : null;

        return new UpdateInfo(version, tagName, releasePage, setup, portable, sums);
    }

    /// <summary>Accepts <c>abcd…</c> and GitHub's <c>sha256:abcd…</c>; anything else is unknown.</summary>
    public static string? ParseSha256Digest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            text = text[7..].Trim();
        if (text.Length != 64)
            return null;

        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
                return null;
        }

        return text.ToLowerInvariant();
    }

    private static Dictionary<string, AssetEntry> ReadAssets(JsonElement root)
    {
        var assets = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array)
            return assets;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(item, "name");
            var url = GetUri(item, "browser_download_url");
            if (string.IsNullOrEmpty(name) || url is null)
                continue;

            var size = item.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsed)
                ? parsed
                : 0;
            var digest = ParseSha256Digest(GetString(item, "digest"));

            // Duplicate names cannot be downloaded unambiguously; the first one wins.
            assets.TryAdd(name, new AssetEntry(url, size, digest));
        }

        return assets;
    }

    private static UpdateAsset? BuildAsset(string name, Dictionary<string, AssetEntry> assets)
    {
        if (!assets.TryGetValue(name, out var entry))
            return null;

        // The signature rides next to the asset as "<asset>.sig": prefer the explicit asset when the
        // release lists it, otherwise use the sibling download URL (the same GitHub path in practice).
        var signatureUrl = assets.TryGetValue(name + ".sig", out var signature)
            ? signature.Url
            : new Uri(entry.Url.AbsoluteUri + ".sig", UriKind.Absolute);

        return new UpdateAsset(name, entry.Url, entry.Size, entry.Digest, signatureUrl);
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Uri? GetUri(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
    }

    private readonly record struct AssetEntry(Uri Url, long Size, string? Digest);
}
