using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>PDF rendering (PDFium), text extraction (PdfPig) and page operations (PDFsharp).</summary>
public static class PdfBackend
{
    public static int PageCount(string path)
    {
        using var stream = File.OpenRead(path);
        return PDFtoImage.Conversion.GetPageCount(stream);
    }

    /// <summary>
    /// Renders every page. A single page becomes "name.png"; more pages go into a folder "name (pages)".
    /// </summary>
    public static async Task<IEnumerable<string>> RenderAsync(FileItem item, string format, OperationContext ctx, IProgress<double> progress)
    {
        var bytes = await File.ReadAllBytesAsync(item.Path, ctx.CancellationToken).ConfigureAwait(false);
        var count = PDFtoImage.Conversion.GetPageCount(bytes);
        if (count <= 0)
            throw new ConversionException(T("error.pdf.noPages"));
        var options = new PDFtoImage.RenderOptions(Dpi: ctx.Settings.PdfDpi, BackgroundColor: SkiaSharp.SKColors.White, WithAnnotations: true, WithFormFill: true);
        var dir = ctx.OutputDirFor(item.Path);

        if (count == 1)
        {
            var single = await OutputPaths.ProduceAsync(dir, item.BaseName, format, tmp =>
                Task.Run(() => SavePage(tmp, bytes, 0, format, options, ctx.Settings.JpegQuality), ctx.CancellationToken)).ConfigureAwait(false);
            return [single];
        }

        var folder = OutputPaths.UniqueDirectory(dir, $"{item.BaseName} ({T("suffix.pages")})");
        Directory.CreateDirectory(folder);
        var digits = count.ToString().Length;
        try
        {
            for (var i = 0; i < count; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(folder, $"{item.BaseName} - {(i + 1).ToString().PadLeft(digits, '0')}.{format}");
                await Task.Run(() => SavePage(path, bytes, i, format, options, ctx.Settings.JpegQuality), ctx.CancellationToken).ConfigureAwait(false);
                progress.Report((i + 1.0) / count);
            }
        }
        catch
        {
            OutputPaths.TryDelete(folder);
            throw;
        }
        return [folder];
    }

    /// <summary>Renders every page to "page-001.png"… in <paramref name="dir"/> (used for OCR).</summary>
    public static IReadOnlyList<string> RenderPagesToPng(string pdf, string dir, int dpi, CancellationToken ct)
    {
        var bytes = File.ReadAllBytes(pdf);
        var count = PDFtoImage.Conversion.GetPageCount(bytes);
        if (count <= 0)
            throw new ConversionException(T("error.pdf.noPages"));
        var options = new PDFtoImage.RenderOptions(Dpi: dpi, BackgroundColor: SkiaSharp.SKColors.White, WithAnnotations: true, WithFormFill: true);
        var pages = new List<string>();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(dir, $"page-{i + 1:000}.png");
            SavePage(path, bytes, i, "png", options, 100);
            pages.Add(path);
        }
        return pages;
    }

    private static void SavePage(string path, byte[] pdf, int page, string format, PDFtoImage.RenderOptions options, int quality)
    {
        using var bitmap = PDFtoImage.Conversion.ToImage(pdf, page, options: options);
        using var file = File.Create(path);
        var encoded = format == "jpg" ? SkiaSharp.SKEncodedImageFormat.Jpeg
            : format == "webp" ? SkiaSharp.SKEncodedImageFormat.Webp
            : SkiaSharp.SKEncodedImageFormat.Png;
        bitmap.Encode(file, encoded, quality);
    }

    /// <summary>Plain text, pages separated by blank lines (or "---" rules for Markdown).</summary>
    public static string ExtractText(string path, bool markdown)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page).Trim();
            if (text.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(markdown ? "\n\n---\n\n" : "\n\n");
            sb.Append(markdown ? ToMarkdownParagraphs(text) : text);
        }
        if (sb.Length == 0)
            throw new ConversionException(T("error.pdf.noText"));
        return sb.ToString();
    }

    private static string ToMarkdownParagraphs(string text)
    {
        // Join wrapped lines into paragraphs; keep blank lines as paragraph breaks.
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var paragraph = new StringBuilder();
        void Flush()
        {
            if (paragraph.Length == 0)
                return;
            sb.Append(paragraph.ToString().Trim()).Append("\n\n");
            paragraph.Clear();
        }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }
            if (paragraph.Length > 0)
            {
                if (paragraph[^1] == '-' && paragraph.Length > 1 && char.IsLetter(paragraph[^2]))
                    paragraph.Length--; // de-hyphenate
                else
                    paragraph.Append(' ');
            }
            paragraph.Append(line);
        }
        Flush();
        return sb.ToString().TrimEnd();
    }

    public static Task<string> ToTextAsync(FileItem item, string format, OperationContext ctx) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
            Task.Run(() => File.WriteAllText(tmp, ExtractText(item.Path, format == "md"), new UTF8Encoding(false)), ctx.CancellationToken));

    /// <summary>PDF → DOCX/HTML/EPUB/ODT: extract text to Markdown, then let pandoc build the document.</summary>
    public static async Task<string> ToDocumentAsync(FileItem item, string format, OperationContext ctx)
    {
        var tempDir = OutputPaths.TempDirectory();
        try
        {
            var md = Path.Combine(tempDir, item.BaseName + ".md");
            await File.WriteAllTextAsync(md, ExtractText(item.Path, markdown: true), new UTF8Encoding(false), ctx.CancellationToken).ConfigureAwait(false);
            return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
                PandocBackend.ConvertAsync(ctx, md, "md", tmp, format)).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(tempDir);
        }
    }

    /// <summary>
    /// PDF → XLSX (tables only): LibreOffice's PDF import (calc_pdf_import) puts table text into cells. A workbook
    /// without cells — or no workbook at all when the filter cannot import anything — means there are no tables.
    /// </summary>
    public static async Task<string> ToXlsxAsync(FileItem item, OperationContext ctx)
    {
        var produced = await OfficeBackend.TryConvertToTempAsync(ctx, item.Path, "xlsx", "calc_pdf_import").ConfigureAwait(false);
        try
        {
            if (produced is not null && HasTableData(produced))
            {
                var finalTable = OutputPaths.Unique(ctx.OutputDirFor(item.Path), item.BaseName, "xlsx");
                File.Move(produced, finalTable);
                return finalTable;
            }

            // Ledger/statement PDFs often have aligned columns but no ruled table, so LibreOffice's
            // PDF-import filter produces an empty workbook. Rebuild the rows from PdfPig word positions.
            var rows = ExtractTextTableRows(item.Path);
            if (rows.Count < 2)
                throw new ConversionException(T("error.pdf.noTables"));
            var final = OutputPaths.Unique(ctx.OutputDirFor(item.Path), item.BaseName, "xlsx");
            SpreadsheetBackend.WriteXlsx([new SpreadsheetBackend.Sheet("Table", rows)], final);
            return final;
        }
        finally
        {
            if (produced is not null)
                OutputPaths.TryDelete(Path.GetDirectoryName(produced)!);
        }
    }

    /// <summary>
    /// Rebuilds rows from word positions for unruled, column-aligned PDFs. A detected header supplies the
    /// column anchors; without one, cells are split on visual gaps.
    /// </summary>
    internal static List<string[]> ExtractTextTableRows(string path)
    {
        var lines = new List<(int Page, double Y, List<Word> Words)>();
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var pageNumber = 0;
        foreach (var page in document.GetPages())
        {
            pageNumber++;
            foreach (var word in page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)))
            {
                var y = page.Height - word.BoundingBox.Bottom;
                var line = lines.FirstOrDefault(candidate =>
                    candidate.Page == pageNumber && Math.Abs(candidate.Y - y) <= 3);
                if (line.Words is null)
                {
                    line = (pageNumber, y, []);
                    lines.Add(line);
                }
                line.Words.Add(word);
            }
        }
        var ordered = lines.OrderBy(line => line.Page).ThenByDescending(line => line.Y)
            .Select(line => line.Words.OrderBy(word => word.BoundingBox.Left).ToList())
            .ToList();

        var header = ordered.FirstOrDefault(IsTableHeader);
        if (header is not null)
        {
            var anchors = header.Select(word => (word.BoundingBox.Left + word.BoundingBox.Right) / 2).ToArray();
            var rows = new List<string[]>();
            var started = false;
            foreach (var line in ordered)
            {
                if (!started)
                {
                    if (!ReferenceEquals(line, header))
                        continue;
                    started = true;
                }
                var cells = new string[anchors.Length];
                foreach (var word in line)
                {
                    var center = (word.BoundingBox.Left + word.BoundingBox.Right) / 2;
                    var column = 0;
                    var best = double.MaxValue;
                    for (var i = 0; i < anchors.Length; i++)
                    {
                        var distance = Math.Abs(center - anchors[i]);
                        if (distance < best)
                        {
                            best = distance;
                            column = i;
                        }
                    }
                    cells[column] = string.IsNullOrEmpty(cells[column])
                        ? word.Text
                        : cells[column] + " " + word.Text;
                }
                if (cells.Count(cell => !string.IsNullOrWhiteSpace(cell)) >= 2)
                    rows.Add(cells);
            }
            // Keep the header and real ledger lines only; page headers/footers and the address block are
            // not part of the table.
            return rows.Where((row, index) => (header is not null && index == 0) ||
                    row.Any(cell => !string.IsNullOrEmpty(cell) &&
                        System.Text.RegularExpressions.Regex.IsMatch(cell, @"\b\d{2}/\d{2}/\d{4}\b")) ||
                    row.Any(cell => cell is not null && cell.Contains("Total", StringComparison.OrdinalIgnoreCase)))
                .Where(row => !row.Any(cell => cell is not null &&
                    (cell.Contains("Printed On", StringComparison.OrdinalIgnoreCase) ||
                     cell.StartsWith("Page ", StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        var generic = new List<string[]>();
        foreach (var line in ordered)
        {
            var cells = new List<string>();
            var current = new StringBuilder();
            Word? previous = null;
            foreach (var word in line)
            {
                if (previous is not null && word.BoundingBox.Left - previous.BoundingBox.Right > 14)
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                }
                if (current.Length > 0)
                    current.Append(' ');
                current.Append(word.Text);
                previous = word;
            }
            cells.Add(current.ToString().Trim());
            if (cells.Count(cell => cell.Length > 0) >= 2)
                generic.Add(cells.ToArray());
        }
        return generic;
    }

    private static bool IsTableHeader(List<Word> line)
    {
        var text = string.Join(' ', line.Select(word => word.Text));
        return text.Contains("Debit", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("Credit", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Balance", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when any sheet has at least one non-blank cell; a PDF import without tables leaves them empty.</summary>
    private static bool HasTableData(string workbook)
    {
        try
        {
            return SpreadsheetBackend.Read(workbook)
                .Any(sheet => sheet.Rows.Any(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A 0-byte or damaged export is not a usable spreadsheet.
            return false;
        }
    }

    public static string Merge(IReadOnlyList<string> inputs, string output)
    {
        using var result = new PdfSharp.Pdf.PdfDocument();
        foreach (var input in inputs)
        {
            using var source = Open(input, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages)
                result.AddPage(page);
        }
        result.Save(output);
        return output;
    }

    public static async Task<IEnumerable<string>> SplitAsync(FileItem item, OperationContext ctx, IProgress<double> progress)
    {
        // Check before creating the output folder, so a zero-page PDF cannot leave an empty "(pages)".
        if (PageCount(item.Path) <= 0)
            throw new ConversionException(T("error.pdf.noPages"));

        var dir = ctx.OutputDirFor(item.Path);
        string? folder = null;
        try
        {
            await Task.Run(() =>
            {
                using var source = Open(item.Path, PdfDocumentOpenMode.Import);
                var count = source.PageCount;
                if (count <= 0)
                    throw new ConversionException(T("error.pdf.noPages"));
                var created = OutputPaths.UniqueDirectory(dir, $"{item.BaseName} ({T("suffix.pages")})");
                folder = created;
                Directory.CreateDirectory(created);
                var digits = count.ToString().Length;
                for (var i = 0; i < count; i++)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    using var single = new PdfSharp.Pdf.PdfDocument();
                    single.AddPage(source.Pages[i]);
                    single.Save(Path.Combine(created, $"{item.BaseName} - {(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
                    progress.Report((i + 1.0) / count);
                }
            }, ctx.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (folder is not null)
                OutputPaths.TryDelete(folder);
            throw;
        }
        return [folder!];
    }

    public static void Rotate(string input, string output, int degrees)
    {
        using var document = Open(input, PdfDocumentOpenMode.Modify);
        foreach (var page in document.Pages)
            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
        document.Save(output);
    }

    /// <summary>
    /// Re-saves with compressed streams and without unused objects. PDFsharp can re-encode an already
    /// compact file into a larger one (the shipped doc.pdf grows 3770 → 5464 bytes), so the optimized
    /// result is only kept when it is actually smaller; otherwise the original bytes are copied through.
    /// The output never grows.
    /// </summary>
    public static void Optimize(string input, string output)
    {
        using (var document = Open(input, PdfDocumentOpenMode.Modify))
        {
            document.Options.CompressContentStreams = true;
            document.Options.NoCompression = false;
            document.Options.EnableCcittCompressionForBilevelImages = true;
            document.Save(output);
        }

        if (new FileInfo(output).Length >= new FileInfo(input).Length)
            File.Copy(input, output, overwrite: true);
    }

    private static PdfSharp.Pdf.PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException(T("error.pdf.cannotOpen", Path.GetFileName(path), ex.Message), ex);
        }
    }
}
