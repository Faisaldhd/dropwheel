using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileAction.Core.Updates;

namespace FileAction.Tests;

/// <summary>
/// Offline tests for the 2.2.0 update core: numeric version rules, GitHub release parsing, the SHA-256
/// helpers, ECDSA verification and the check/download state machine. No network, no real GitHub and no
/// production keys: the signature tests generate ephemeral P-256 pairs inside the test.
/// </summary>
public class UpdateCoreTests
{
    private const string Hash64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string UpperHash64 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string SetupName = "DropWheel-2.3.0-Setup.exe";
    private const string PortableName = "DropWheel-2.3.0-portable-win-x64.zip";

    // ---------------------------------------------------------------- SemanticVersion

    [Theory]
    [InlineData("2.10.0", "2.9.0", true)]
    [InlineData("2.2.0", "1.9.9", true)]
    [InlineData("2.2.0", "2.2.0", false)]
    [InlineData("2.9.0", "2.10.0", false)]
    [InlineData("v2.3.0", "2.2.9", true)]
    public void Comparison_is_numeric_not_lexicographic(string candidate, string current, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(candidate, out var candidateVersion), candidate);
        Assert.True(SemanticVersion.TryParse(current, out var currentVersion), current);

        Assert.Equal(expected, SemanticVersion.IsNewer(candidateVersion, currentVersion));
    }

    [Fact]
    public void Parsed_versions_compare_equal_across_prefixes_and_missing_parts()
    {
        Assert.True(SemanticVersion.TryParse("2.2.0", out var threeParts));
        Assert.True(SemanticVersion.TryParse("v2.2.0", out var prefixed));
        Assert.True(SemanticVersion.TryParse("2.2", out var twoParts));
        Assert.True(SemanticVersion.TryParse("2.2.0.0", out var fourParts));

        Assert.Equal(new Version(2, 2, 0, 0), threeParts);
        Assert.Equal(new Version(2, 2, 0, 0), twoParts);
        Assert.Equal(0, SemanticVersion.Compare(threeParts, prefixed));
        Assert.Equal(0, SemanticVersion.Compare(twoParts, threeParts));
        Assert.Equal(0, SemanticVersion.Compare(fourParts, threeParts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("2.3.0-beta.1")]
    [InlineData("2.3.0-rc1")]
    [InlineData("abc")]
    [InlineData("2..0")]
    public void TryParse_rejects_prereleases_and_malformed_tags(string? tag)
    {
        Assert.False(SemanticVersion.TryParse(tag, out _));
        Assert.False(SemanticVersion.IsStable(tag));
    }

    [Fact]
    public void Build_metadata_is_ignored_but_a_prerelease_suffix_is_rejected()
    {
        Assert.True(SemanticVersion.TryParse("v2.3.0+sha.abc123", out var version));
        Assert.Equal(new Version(2, 3, 0, 0), version);
        Assert.True(SemanticVersion.IsStable("2.3.0+build.5"));

        Assert.False(SemanticVersion.IsStable("2.3.0-beta.1+build.5"));
    }

    // ---------------------------------------------------------------- GitHub digests

    [Theory]
    [InlineData(Hash64, Hash64)]
    [InlineData(UpperHash64, Hash64)]
    [InlineData("sha256:" + Hash64, Hash64)]
    [InlineData("SHA256:" + UpperHash64, Hash64)]
    [InlineData(" sha256:" + Hash64 + " ", Hash64)]
    public void ParseSha256Digest_accepts_plain_and_prefixed_hex(string value, string expected) =>
        Assert.Equal(expected, GitHubReleaseClient.ParseSha256Digest(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("md5:" + Hash64)]
    [InlineData("sha256:not-a-hash")]
    [InlineData("sha256:0123")]
    [InlineData(Hash64 + "0")]
    public void ParseSha256Digest_rejects_missing_or_malformed_values(string? value) =>
        Assert.Null(GitHubReleaseClient.ParseSha256Digest(value));

    // ---------------------------------------------------------------- SHA256SUMS

    [Fact]
    public void Find_reads_sha256sum_lines_with_binary_markers_and_crlf()
    {
        var text = $"{Hash64}  {SetupName}\r\n{new string('b', 64)}  other.exe\n";

        Assert.Equal(Hash64, Sha256Sums.Find(text, SetupName));
        Assert.Equal(Hash64, Sha256Sums.Find($"{Hash64} *{SetupName}\n", SetupName));
        Assert.Equal(Hash64, Sha256Sums.Find($"{UpperHash64}  {SetupName}", SetupName));
        Assert.Null(Sha256Sums.Find(text, "missing.exe"));
        Assert.Null(Sha256Sums.Find(text, ""));
        Assert.Null(Sha256Sums.Find(null, SetupName));
        Assert.Null(Sha256Sums.Find("", SetupName));
    }

    [Fact]
    public void Matches_requires_the_listed_hash_to_equal_the_actual_hash()
    {
        var text = $"{Hash64}  {SetupName}\n";

        Assert.True(Sha256Sums.Matches(text, SetupName, Hash64));
        Assert.True(Sha256Sums.Matches(text, SetupName, UpperHash64));
        Assert.False(Sha256Sums.Matches(text, SetupName, new string('b', 64)));
        Assert.False(Sha256Sums.Matches(text, "missing.exe", Hash64));
        Assert.False(Sha256Sums.Matches(null, SetupName, Hash64));
        Assert.False(Sha256Sums.Matches(text, SetupName, null));
    }

    // ---------------------------------------------------------------- GitHubReleaseClient.ParseLatest

    [Fact]
    public void ParseLatest_reads_a_real_shaped_release()
    {
        var info = GitHubReleaseClient.ParseLatest(ReleaseJson("v2.3.0", "2.3.0"));
        Assert.NotNull(info);
        var release = info!;

        Assert.Equal(new Version(2, 3, 0), release.Version);
        Assert.Equal("2.3.0", release.Version.ToString());
        Assert.Equal("v2.3.0", release.TagName);
        Assert.Equal(
            new Uri("https://github.com/Faisaldhd/dropwheel/releases/tag/v2.3.0"), release.ReleasePage);

        Assert.NotNull(release.Setup);
        var setup = release.Setup!;
        Assert.Equal(SetupName, setup.Name);
        Assert.Equal(new Uri("https://github.com/Faisaldhd/dropwheel/releases/download/v2.3.0/" + SetupName), setup.DownloadUrl);
        Assert.Equal(12_345L, setup.Size);
        Assert.Equal(Hash64, setup.Sha256Digest);
        Assert.Equal(
            new Uri("https://github.com/Faisaldhd/dropwheel/releases/download/v2.3.0/" + SetupName + ".sig"),
            setup.SignatureUrl);

        Assert.NotNull(release.Portable);
        var portable = release.Portable!;
        Assert.Equal(PortableName, portable.Name);
        Assert.Equal(67_890L, portable.Size);
        Assert.Equal(Hash64, portable.Sha256Digest);

        Assert.Equal(
            new Uri("https://github.com/Faisaldhd/dropwheel/releases/download/v2.3.0/SHA256SUMS.txt"),
            release.Sha256SumsUrl);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ParseLatest_ignores_draft_and_prerelease_flags(bool draft, bool prerelease) =>
        Assert.Null(GitHubReleaseClient.ParseLatest(ReleaseJson("v2.3.0", "2.3.0", draft, prerelease)));

    [Fact]
    public void ParseLatest_ignores_a_prerelease_tag() =>
        Assert.Null(GitHubReleaseClient.ParseLatest(ReleaseJson("v2.3.0-beta.1", "2.3.0")));

    // ---------------------------------------------------------------- UpdateVerifier

    [Fact]
    public void Sha256Hex_returns_the_lower_case_digest()
    {
        var bytes = Encoding.UTF8.GetBytes("update payload");
        using var temp = new TempDirectory();
        var file = temp.Write("payload.bin", bytes);

        Assert.Equal(Sha256Hex(bytes), UpdateVerifier.Sha256Hex(file));
        Assert.Equal(64, UpdateVerifier.Sha256Hex(file).Length);
    }

    [Fact]
    public void VerifyDigest_accepts_the_matching_digest_only()
    {
        var bytes = Encoding.UTF8.GetBytes("update payload");
        var hash = Sha256Hex(bytes);
        using var temp = new TempDirectory();
        var file = temp.Write("payload.bin", bytes);

        Assert.True(UpdateVerifier.VerifyDigest(file, hash));
        Assert.True(UpdateVerifier.VerifyDigest(file, hash.ToUpperInvariant()));
        Assert.False(UpdateVerifier.VerifyDigest(file, new string('0', 64)));
        Assert.False(UpdateVerifier.VerifyDigest(file, null));
        Assert.False(UpdateVerifier.VerifyDigest(file, "not-a-digest"));
        Assert.False(UpdateVerifier.VerifyDigest(Path.Combine(temp.Root, "missing.bin"), hash));
    }

    [Fact]
    public void VerifySignature_accepts_p1363_and_der_and_rejects_tampering_and_wrong_keys()
    {
        var bytes = Encoding.UTF8.GetBytes("signed update payload");
        var hash = SHA256.HashData(bytes);
        using var temp = new TempDirectory();
        var file = temp.Write("payload.bin", bytes);

        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        var p1363 = Convert.ToBase64String(
            signer.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var der = Convert.ToBase64String(
            signer.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence));

        Assert.True(UpdateVerifier.VerifySignature(file, p1363, publicKey));
        Assert.True(UpdateVerifier.VerifySignature(file, der, publicKey));

        var tampered = temp.Write("tampered.bin", Encoding.UTF8.GetBytes("signed update payloaD"));
        Assert.False(UpdateVerifier.VerifySignature(tampered, p1363, publicKey));

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherPublicKey = Convert.ToBase64String(otherKey.ExportSubjectPublicKeyInfo());
        Assert.False(UpdateVerifier.VerifySignature(file, p1363, otherPublicKey));

        Assert.False(UpdateVerifier.VerifySignature(file, null, publicKey));
        Assert.False(UpdateVerifier.VerifySignature(file, "", publicKey));
        Assert.False(UpdateVerifier.VerifySignature(file, "not base64!", publicKey));
        Assert.False(UpdateVerifier.VerifySignature(file, p1363, "not a key"));
        Assert.False(UpdateVerifier.VerifySignature(Path.Combine(temp.Root, "missing.bin"), p1363, publicKey));
    }

    [Fact]
    public void Verify_requires_the_signature_and_every_supplied_digest_source()
    {
        var bytes = Encoding.UTF8.GetBytes("signed update payload");
        var hash = Sha256Hex(bytes);
        using var temp = new TempDirectory();
        var file = temp.Write("payload.bin", bytes);

        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        var signature = Convert.ToBase64String(
            signer.SignHash(SHA256.HashData(bytes), DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var sums = $"{hash}  payload.bin\n";

        Assert.True(UpdateVerifier.Verify(file, hash, sums, signature, publicKey));
        Assert.True(UpdateVerifier.Verify(file, null, null, signature, publicKey));

        Assert.False(UpdateVerifier.Verify(file, new string('a', 64), sums, signature, publicKey));
        Assert.False(UpdateVerifier.Verify(file, hash, $"{new string('b', 64)}  payload.bin\n", signature, publicKey));
        Assert.False(UpdateVerifier.Verify(file, hash, sums, null, publicKey));
        Assert.False(UpdateVerifier.Verify(file, hash, sums, "garbage", publicKey));
        Assert.False(UpdateVerifier.Verify(file, hash, sums, signature, "not a key"));
    }

    // ---------------------------------------------------------------- UpdateService.CheckAsync

    [Fact]
    public async Task Check_returns_Available_for_a_newer_release()
    {
        var handler = JsonHandler(ReleaseJson("v2.3.0", "2.3.0"));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);

        var notifications = 0;
        service.UpdateAvailable += _ => notifications++;

        var status = await service.CheckAsync(userInitiated: true, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Available, status);
        Assert.NotNull(service.Available);
        var info = service.Available!;
        Assert.Equal(new Version(2, 3, 0), info.Version);
        Assert.Equal("v2.3.0", info.TagName);
        Assert.Equal(SetupName, service.AssetFor(info)!.Name);
        Assert.Equal(1, notifications);
        Assert.False(service.IsChecking);
        Assert.Null(service.LastError);

        Assert.Single(handler.RequestedUris);
        Assert.Equal(new Uri(UpdateConfig.LatestReleaseUrl), handler.RequestedUris[0]);
        Assert.Contains("DropWheel", handler.UserAgents[0]);
    }

    [Theory]
    [InlineData(false, SetupName)]
    [InlineData(true, PortableName)]
    public async Task Check_selects_the_asset_for_the_install_kind(bool portable, string expectedName)
    {
        var handler = JsonHandler(ReleaseJson("v2.3.0", "2.3.0"));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), portable, new HttpClient(handler), temp.Root);

        var status = await service.CheckAsync(userInitiated: false, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Available, status);
        Assert.Equal(portable, service.IsPortable);
        Assert.Equal(expectedName, service.AssetFor(service.Available!)!.Name);
    }

    [Theory]
    [InlineData("v2.2.0", "2.2.0")]
    [InlineData("v1.9.9", "1.9.9")]
    public async Task Check_returns_UpToDate_for_the_same_or_an_older_release(string tag, string version)
    {
        var handler = JsonHandler(ReleaseJson(tag, version));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);

        var status = await service.CheckAsync(userInitiated: true, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.UpToDate, status);
        Assert.Null(service.Available);
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task Check_returns_Failed_on_a_forbidden_response()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(StatusResponse(HttpStatusCode.Forbidden)));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);

        var status = await service.CheckAsync(userInitiated: true, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, status);
        Assert.NotNull(service.LastError);
        Assert.Null(service.Available);
        Assert.False(service.IsChecking);
    }

    [Fact]
    public async Task Check_returns_Failed_when_the_transport_throws()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("offline"));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);

        var status = await service.CheckAsync(userInitiated: false, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, status);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task Check_returns_Failed_on_malformed_json()
    {
        var handler = JsonHandler("{ this is not a release payload");
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);

        var status = await service.CheckAsync(userInitiated: true, CancellationToken.None);

        Assert.Equal(UpdateCheckStatus.Failed, status);
        Assert.NotNull(service.LastError);
        Assert.Null(service.Available);
    }

    [Fact]
    public async Task Check_propagates_caller_cancellation()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("the check must not start"));
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckAsync(userInitiated: true, cancellation.Token));

        Assert.False(service.IsChecking);
        Assert.Null(service.LastError);
    }

    // ---------------------------------------------------------------- UpdateService.DownloadAsync

    [Fact]
    public async Task Download_fails_closed_when_the_GitHub_digest_is_missing()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0),
            isPortable: false,
            http: new HttpClient(DownloadHandler(SetupName, payload, "garbage", $"{hash}  {SetupName}\n")),
            stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, digest: null, signatureUrl: SignatureUri(SetupName));

        var result = await service.DownloadAsync(info, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(UpdateService.VerificationFailure, service.LastError);
        Assert.False(service.IsDownloading);
        AssertNoStagedFiles(temp.Root);
    }

    [Fact]
    public async Task Download_fails_closed_when_SHA256SUMS_does_not_list_the_asset()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0),
            isPortable: false,
            http: new HttpClient(
                DownloadHandler(SetupName, payload, "garbage", $"{hash}  Some-Other.exe\n")),
            stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, hash, SignatureUri(SetupName));

        var result = await service.DownloadAsync(info, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(UpdateService.VerificationFailure, service.LastError);
        AssertNoStagedFiles(temp.Root);
    }

    [Fact]
    public async Task Download_fails_closed_when_the_signature_does_not_verify()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0),
            isPortable: false,
            http: new HttpClient(
                DownloadHandler(SetupName, payload, "not base64!", $"{hash}  {SetupName}\n")),
            stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, hash, SignatureUri(SetupName));

        var result = await service.DownloadAsync(info, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(UpdateService.VerificationFailure, service.LastError);
        AssertNoStagedFiles(temp.Root);
    }

    [Fact]
    public async Task Download_fails_closed_when_the_sig_asset_is_missing()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0),
            isPortable: false,
            http: new HttpClient(DownloadHandler(SetupName, payload, "garbage", $"{hash}  {SetupName}\n")),
            stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, hash, signatureUrl: null);

        var result = await service.DownloadAsync(info, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(UpdateService.VerificationFailure, service.LastError);
        AssertNoStagedFiles(temp.Root);
    }

    [Fact]
    public async Task Download_fails_closed_when_the_sig_download_returns_404()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        using var service = new UpdateService(
            new Version(2, 2, 0),
            isPortable: false,
            http: new HttpClient(
                DownloadHandler(SetupName, payload, signature: null, sums: $"{hash}  {SetupName}\n")),
            stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, hash, SignatureUri(SetupName));

        var result = await service.DownloadAsync(info, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(UpdateService.VerificationFailure, service.LastError);
        AssertNoStagedFiles(temp.Root);
    }

    [Fact]
    public async Task Download_propagates_caller_cancellation_without_leaving_files()
    {
        var payload = Encoding.UTF8.GetBytes("not really a setup");
        var hash = Sha256Hex(payload);
        using var temp = new TempDirectory();
        var handler = new FakeHandler((_, _) =>
            throw new InvalidOperationException("no download must start after cancellation"));
        using var service = new UpdateService(
            new Version(2, 2, 0), isPortable: false, http: new HttpClient(handler), stagingRoot: temp.Root);
        var info = DownloadInfo(SetupName, hash, SignatureUri(SetupName));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadAsync(info, null, cancellation.Token));

        Assert.False(service.IsDownloading);
        AssertNoStagedFiles(temp.Root);
    }

    // ---------------------------------------------------------------- helpers

    private static Uri SignatureUri(string fileName) =>
        new("https://example.test/downloads/" + fileName + ".sig", UriKind.Absolute);

    private static UpdateInfo DownloadInfo(string fileName, string? digest, Uri? signatureUrl = null)
    {
        var asset = new UpdateAsset(
            fileName,
            new Uri("https://example.test/downloads/" + fileName, UriKind.Absolute),
            0,
            digest,
            signatureUrl);
        return new UpdateInfo(
            new Version(2, 3, 0),
            "v2.3.0",
            new Uri("https://example.test/releases/tag/v2.3.0", UriKind.Absolute),
            asset,
            null,
            new Uri("https://example.test/downloads/SHA256SUMS.txt", UriKind.Absolute));
    }

    private static FakeHandler DownloadHandler(
        string fileName, byte[] payload, string? signature, string sums) =>
        new((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path.EndsWith("/" + fileName + ".sig", StringComparison.Ordinal))
                response = signature is null ? StatusResponse(HttpStatusCode.NotFound) : TextResponse(signature);
            else if (path.EndsWith("/" + fileName, StringComparison.Ordinal))
                response = BytesResponse(payload);
            else if (path.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
                response = TextResponse(sums);
            else
                response = StatusResponse(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        });

    private static FakeHandler JsonHandler(string json) =>
        new((_, _) => Task.FromResult(JsonResponse(json)));

    private static string ReleaseJson(
        string tag, string version, bool draft = false, bool prerelease = false)
    {
        var downloadRoot = "https://github.com/Faisaldhd/dropwheel/releases/download/" + tag + "/";
        var setupName = $"DropWheel-{version}-Setup.exe";
        var portableName = $"DropWheel-{version}-portable-win-x64.zip";
        var assets = new object[]
        {
            new
            {
                name = setupName,
                browser_download_url = downloadRoot + setupName,
                size = 12_345L,
                digest = "sha256:" + Hash64,
            },
            new
            {
                name = portableName,
                browser_download_url = downloadRoot + portableName,
                size = 67_890L,
                digest = "sha256:" + Hash64,
            },
            new
            {
                name = setupName + ".sig",
                browser_download_url = downloadRoot + setupName + ".sig",
                size = 96L,
                digest = (string?)null,
            },
            new
            {
                name = portableName + ".sig",
                browser_download_url = downloadRoot + portableName + ".sig",
                size = 96L,
                digest = (string?)null,
            },
            new
            {
                name = "SHA256SUMS.txt",
                browser_download_url = downloadRoot + "SHA256SUMS.txt",
                size = 200L,
                digest = (string?)null,
            },
        };

        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            html_url = "https://github.com/Faisaldhd/dropwheel/releases/tag/" + tag,
            assets,
        });
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void AssertNoStagedFiles(string stagingRoot)
    {
        var stage = Path.Combine(stagingRoot, "2.3.0");
        if (!Directory.Exists(stage))
            return;

        Assert.Empty(Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage TextResponse(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "text/plain"),
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private static HttpResponseMessage StatusResponse(HttpStatusCode status) => new(status);

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri)
                RequestedUris.Add(uri);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return responder(request, cancellationToken);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "dropwheel-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
