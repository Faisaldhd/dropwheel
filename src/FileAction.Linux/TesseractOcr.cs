using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;
using static FileAction.Core.Loc;

namespace FileAction.Linux;

/// <summary>"Extract text" on Linux: OCR with tesseract (optional dependency).</summary>
public sealed class TesseractOcr : Operation
{
    private static readonly Lazy<string?> Exe = new(() => FindInPath("tesseract"));
    private readonly LinuxSettings _settings;

    public TesseractOcr(LinuxSettings settings)
        : base("tools.ocr", WheelKind.Tools, T("Text z obrázku", "Extract text"), Glyphs.Text, "OCR")
    {
        _settings = settings;
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) =>
        items.Count > 0 && items.All(i => !i.IsDirectory && (Formats.Images.Contains(i.Format) || i.Format == "pdf"));

    public override bool IsAvailable(ToolLocator tools, IReadOnlyList<FileItem> items) => Exe.Value is not null;

    public override async Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx)
    {
        var exe = Exe.Value ?? throw new ConversionException(T("Chybí tesseract (sudo pacman -S tesseract tesseract-data-eng).", "tesseract is missing (sudo pacman -S tesseract tesseract-data-eng)."));
        var languages = await LanguagesAsync(exe, ctx.CancellationToken);
        var outputs = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var temp = OutputPaths.TempDirectory();
            try
            {
                IReadOnlyList<string> pages;
                if (item.Format == "pdf")
                {
                    pages = await Task.Run(() => PdfBackend.RenderPagesToPng(item.Path, temp, 300, ctx.CancellationToken), ctx.CancellationToken);
                }
                else
                {
                    var png = Path.Combine(temp, "image.png");
                    await ImageBackend.ConvertAsync(item.Path, png, "png", ctx.Settings, ctx.CancellationToken);
                    pages = [png];
                }
                var text = new StringBuilder();
                for (var p = 0; p < pages.Count; p++)
                {
                    var args = new List<string> { pages[p], "stdout" };
                    if (languages.Length > 0)
                        args.AddRange(["-l", languages]);
                    var pageText = (await ProcessRunner.RunAsync(exe, args, ctx.CancellationToken)).Trim();
                    if (pageText.Length > 0)
                        text.Append(text.Length > 0 ? "\n\n" : "").Append(pageText);
                    ctx.Progress.Report((i + (p + 1.0) / pages.Count) / items.Count);
                }
                if (text.Length == 0)
                    throw new ConversionException(T("V souboru se nenašel žádný text.", "No text was found in the file."));
                outputs.Add(await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("text", "text")})", "txt",
                    tmp => File.WriteAllTextAsync(tmp, text.ToString(), new UTF8Encoding(false), ctx.CancellationToken)));
            }
            finally
            {
                OutputPaths.TryDelete(temp);
            }
        }
        return OperationResult.Of(outputs);
    }

    /// <summary>The configured languages, or the user's language plus English when those packs are installed.</summary>
    private async Task<string> LanguagesAsync(string exe, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_settings.OcrLanguages))
            return _settings.OcrLanguages.Trim();
        try
        {
            var list = await ProcessRunner.RunAsync(exe, ["--list-langs"], ct);
            var installed = list.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var wanted = new[] { Loc.Current == Language.Czech ? "ces" : "", "eng" }.Where(l => l.Length > 0 && installed.Contains(l));
            return string.Join('+', wanted);
        }
        catch (ConversionException)
        {
            return "";
        }
    }

    public static string? FindInPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
}
