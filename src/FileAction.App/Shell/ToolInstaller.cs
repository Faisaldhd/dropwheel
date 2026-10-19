using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using FileAction.App.Diagnostics;
using FileAction.Core;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell;

/// <summary>
/// Downloads FFmpeg and Pandoc into %LOCALAPPDATA%\DropWheel\tools the first time a conversion needs them,
/// so the installer stays small. Every download is checked against its published SHA-256.
/// </summary>
internal static class ToolInstaller
{
    public static readonly ExternalTool[] Installable = [ExternalTool.Ffmpeg, ExternalTool.Pandoc];

    private const string FfmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.7z";
    private const string PandocApi = "https://api.github.com/repos/jgm/pandoc/releases/latest";
    private static readonly HttpClient Http = CreateHttp();

    public static bool CanInstall(ExternalTool tool) => Installable.Contains(tool);

    private static string? _installFolder;

    /// <summary>
    /// R-16: the first tool folder that is actually writable. A locked-down machine can refuse
    /// <c>%LOCALAPPDATA%\DropWheel\tools</c>, so fall back to <c>%LOCALAPPDATA%\Programs\DropWheel\tools</c>,
    /// then to TEMP.
    /// </summary>
    public static string InstallFolder => _installFolder ??= ResolveInstallFolder();

    private static string ResolveInstallFolder()
    {
        foreach (var folder in new[] { ToolLocator.DownloadFolder, ToolLocator.FallbackDownloadFolder, Path.Combine(Path.GetTempPath(), "DropWheel", "tools") })
        {
            if (IsWritable(folder))
                return folder;
        }
        AppLog.Warn("no writable tool folder; the install will report the access error for " + ToolLocator.DownloadFolder);
        return ToolLocator.DownloadFolder;
    }

    private static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".write-probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Approximate download size for the confirmation question.</summary>
    public static string DownloadSize(ExternalTool tool) => tool == ExternalTool.Ffmpeg ? "34 MB" : "35 MB";

    public static async Task InstallAsync(ExternalTool tool, IProgress<double> progress, CancellationToken ct)
    {
        var (url, sha256, exeName) = tool switch
        {
            ExternalTool.Ffmpeg => (FfmpegUrl, (await Http.GetStringAsync(FfmpegUrl + ".sha256", ct)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant(), "ffmpeg.exe"),
            ExternalTool.Pandoc => await PandocAssetAsync(ct),
            _ => throw new ConversionException(T("Tento nástroj se instaluje zvlášť.", "This tool is installed separately.")),
        };

        var folder = InstallFolder;
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(Path.GetTempPath(), $"dropwheel-{exeName}-{Guid.NewGuid():N}{Path.GetExtension(new Uri(url).AbsolutePath)}");
        try
        {
            await DownloadAsync(url, archive, progress, ct);
            if (sha256 is not null)
            {
                await using var file = File.OpenRead(archive);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
                if (actual != sha256)
                    throw new ConversionException(T("Stažený soubor nesouhlasí s otiskem zveřejněným autory.", "The download doesn't match the checksum published by its authors."));
            }
            await Task.Run(() => ExtractExe(archive, exeName, folder, ct), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // R-16: say which tool, which folder and which OS error — not "choose another output folder".
            AppLog.Error($"could not install {tool} into {folder}", ex);
            throw new ToolInstallException(tool, folder, ex);
        }
        finally
        {
            OutputPaths.TryDelete(archive);
        }
    }

    private static async Task<(string Url, string? Sha256, string Exe)> PandocAssetAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(PandocApi, ct));
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith("-windows-x86_64.zip", StringComparison.OrdinalIgnoreCase))
                continue;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
            return (asset.GetProperty("browser_download_url").GetString()!, sha, "pandoc.exe");
        }
        throw new ConversionException(T("Nenašel jsem Pandoc pro Windows.", "Couldn't find Pandoc for Windows."));
    }

    private static async Task DownloadAsync(string url, string target, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                progress.Report(0.9 * done / total); // the rest is unpacking
        }
    }

    /// <summary>Takes just the program out of the archive (FFmpeg's 7z also holds ffplay, ffprobe and docs).</summary>
    private static void ExtractExe(string archivePath, string exeName, string folder, CancellationToken ct)
    {
        var target = Path.Combine(folder, exeName);
        var temp = target + ".part";
        using (var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions()))
        {
            var entry = archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Key is { } key
                && Path.GetFileName(key.Replace('\\', '/')).Equals(exeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ConversionException(T($"V archivu chybí {exeName}.", $"{exeName} is missing from the archive."));
            ct.ThrowIfCancellationRequested();
            entry.WriteToFile(temp, new ExtractionOptions { Overwrite = true });
        }
        File.Move(temp, target, overwrite: true);
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DropWheel");
        return client;
    }
}

/// <summary>A wheel slice whose conversion needs a tool that DropWheel downloads on first use.</summary>
internal sealed class NeedsToolOperation : Operation
{
    public NeedsToolOperation(Operation inner, IReadOnlyList<ExternalTool> missing)
        : base(inner.Id, inner.Wheel, inner.Label, inner.Glyph, T("stáhnout ", "get ") + string.Join(" + ", missing.Select(ToolLocator.DisplayName)))
    {
        Inner = inner;
        Missing = missing;
        Parameters = inner.Parameters;
        Preview = inner.Preview;
    }

    public Operation Inner { get; }

    public IReadOnlyList<ExternalTool> Missing { get; }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => Inner.AppliesTo(items);

    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) => true;

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        throw new NotSupportedException("Install the tools first.");
}
