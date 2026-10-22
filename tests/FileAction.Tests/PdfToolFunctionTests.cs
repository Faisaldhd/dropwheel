using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using FileAction.App.UI.Tools.Pdf;
using FileAction.Core;
using FileAction.Core.Backends;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig;
using Xunit;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using SharpDocument = PdfSharp.Pdf.PdfDocument;
using SharpFont = PdfSharp.Drawing.XFont;
using SharpPage = PdfSharp.Pdf.PdfPage;

namespace FileAction.Tests;

/// <summary>
/// Function tests for the 2.4.0 PDF tools: they generate real PDFs with PDFsharp, run the real merge and
/// organize writers, and assert the produced files (page count, page order, rotation, extractable text and
/// the same-width option) plus the failure paths that must not leave a partial output behind.
/// </summary>
public sealed class PdfToolFunctionTests : IDisposable
{
    private static readonly bool FontsReady = InitializeFonts();

    private readonly string _directory;

    public PdfToolFunctionTests()
    {
        _ = FontsReady;
        // A per-test folder under the process temp directory; never next to the repository.
        _directory = Path.Combine(Path.GetTempPath(), "DropWheelTests", "pdf-tools", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
            // A failed test may still hold the ACL deny; the temp folder is disposable anyway.
        }
    }

    /// <summary>PDFsharp Core needs permission to use the Windows fonts when drawing test text.</summary>
    private static bool InitializeFonts()
    {
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        return true;
    }

    // ------------------------------------------------------------------ fixtures

    /// <summary>Creates a PDF whose pages carry <paramref name="pages"/> (text, rotation, size).</summary>
    private string CreatePdf(string name, params (string Text, int Rotation, double Width, double Height)[] pages)
    {
        var path = Path.Combine(_directory, name);
        using var document = new SharpDocument();
        foreach (var (text, rotation, width, height) in pages)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(width);
            page.Height = XUnit.FromPoint(height);
            page.Rotate = rotation;
            using var graphics = XGraphics.FromPdfPage(page);
            var font = new SharpFont("Arial", 18);
            graphics.DrawString(text, font, XBrushes.Black, new XPoint(24, 60));
        }
        document.Save(path);
        return path;
    }

    /// <summary>Creates a password-protected PDF; opening it without the password must fail.</summary>
    private string CreateEncryptedPdf(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        using var document = new SharpDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(400);
        var font = new SharpFont("Arial", 18);
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawString(text, font, XBrushes.Black, new XPoint(24, 60));

        document.SecuritySettings.UserPassword = "dropwheel-user";
        document.SecuritySettings.OwnerPassword = "dropwheel-owner";
        document.Save(path);
        return path;
    }

    private string Output(string name) => Path.Combine(_directory, name);

    /// <summary>
    /// A hand-written one-page PDF with no compression and no spare objects. PDFsharp's re-save of it is
    /// larger than the source, which is the "optimize grows the file" case the backend must not keep.
    /// </summary>
    private string CreateMinimalPdf(string name)
    {
        const string Catalog = "<< /Type /Catalog /Pages 2 0 R >>";
        const string Pages = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
        const string Page = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>";
        const string Contents = "<< /Length 0 >>\nstream\n\nendstream";
        string[] objects = [Catalog, Pages, Page, Contents];

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>(objects.Length);
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(builder.Length);
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append(offset.ToString("0000000000")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n").Append(xref).Append("\n%%EOF\n");

        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
        return path;
    }

    private static string[] PageTexts(string pdf)
    {
        using var document = PigDocument.Open(pdf);
        return document.GetPages().Select(page => page.Text.Trim()).ToArray();
    }

    /// <summary>The page size and rotation of a written PDF, copied before the reader is disposed.</summary>
    private static (double Width, double Height, int Rotate)[] SharpPages(string pdf)
    {
        using var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return document.Pages
            .Cast<SharpPage>()
            .Select(page => (page.Width.Point, page.Height.Point, page.Rotate))
            .ToArray();
    }

    private static Task WriteAsync(string directory, string baseName, Action<string> write) =>
        OutputPaths.ProduceAsync(directory, baseName, "pdf", temporary => Task.Run(() => write(temporary)));

    // ------------------------------------------------------------------ merge

    [Fact]
    public void Merge_writes_the_three_pdfs_in_document_order_with_their_text()
    {
        var alpha = CreatePdf("alpha.pdf", ("AlphaText", 0, 300, 400));
        var beta = CreatePdf("beta.pdf", ("BetaOne", 0, 300, 400), ("BetaTwo", 0, 300, 400));
        var gamma = CreatePdf("gamma.pdf", ("GammaText", 0, 300, 400));
        var output = Output("Merged.pdf");

        PdfMergeWriter.Write(
            [
                new PdfMergeSource(alpha, PdfPlanKind.Pdf),
                new PdfMergeSource(beta, PdfPlanKind.Pdf),
                new PdfMergeSource(gamma, PdfPlanKind.Pdf),
            ],
            output);

        Assert.True(File.Exists(output), "the merge did not write its output");
        var texts = PageTexts(output);
        Assert.Equal(4, texts.Length);
        Assert.Contains("AlphaText", texts[0], StringComparison.Ordinal);
        Assert.Contains("BetaOne", texts[1], StringComparison.Ordinal);
        Assert.Contains("BetaTwo", texts[2], StringComparison.Ordinal);
        Assert.Contains("GammaText", texts[3], StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_takes_a_page_range_in_document_order()
    {
        var alpha = CreatePdf("range-alpha.pdf", ("AlphaText", 0, 300, 400));
        var beta = CreatePdf("range-beta.pdf",
            ("BetaOne", 0, 300, 400), ("BetaTwo", 0, 300, 400),
            ("BetaThree", 0, 300, 400), ("BetaFour", 0, 300, 400));
        var output = Output("Ranged.pdf");

        PdfMergeWriter.Write(
            [
                new PdfMergeSource(alpha, PdfPlanKind.Pdf),
                // "4, 2-3" names the pages out of order on purpose: the document order must win.
                new PdfMergeSource(beta, PdfPlanKind.Pdf) { PageRange = "4, 2-3" },
            ],
            output);

        var texts = PageTexts(output);
        Assert.Equal(4, texts.Length);
        Assert.Contains("AlphaText", texts[0], StringComparison.Ordinal);
        Assert.Contains("BetaTwo", texts[1], StringComparison.Ordinal);
        Assert.Contains("BetaThree", texts[2], StringComparison.Ordinal);
        Assert.Contains("BetaFour", texts[3], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("2-1")]
    [InlineData("last")]
    public void Merge_fails_on_an_unusable_page_range_without_a_partial_file(string range)
    {
        var source = CreatePdf("two-pages.pdf", ("One", 0, 300, 400), ("Two", 0, 300, 400));
        var directory = Directory.CreateDirectory(Output("range-out-" + Guid.NewGuid().ToString("N"))).FullName;

        var error = Assert.Throws<ConversionException>(() =>
            WriteAsync(directory, "Merged", temporary => PdfMergeWriter.Write(
                [new PdfMergeSource(source, PdfPlanKind.Pdf) { PageRange = range }],
                temporary)).GetAwaiter().GetResult());

        Assert.Contains("two-pages.pdf", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public void Merge_ignores_a_page_range_on_an_image()
    {
        var pdf = CreatePdf("range-image-source.pdf", ("WithImage", 0, 300, 400));
        var image = Output("range-image.png");
        using (var bitmap = new ImageMagick.MagickImage(ImageMagick.MagickColors.RebeccaPurple, 320, 200))
            bitmap.Write(image);
        var output = Output("RangedWithImage.pdf");

        PdfMergeWriter.Write(
            [
                new PdfMergeSource(pdf, PdfPlanKind.Pdf),
                new PdfMergeSource(image, PdfPlanKind.Image) { PageRange = "not a range" },
            ],
            output);

        Assert.Equal(2, PageTexts(output).Length);
    }

    [Fact]
    public void Merge_keeps_the_rotation_of_each_source_page()
    {
        var portrait = CreatePdf("upright.pdf", ("Upright", 0, 300, 400));
        var sideways = CreatePdf("sideways.pdf", ("Sideways", 90, 300, 400), ("UpsideDown", 180, 300, 400));
        var output = Output("Rotated.pdf");

        PdfMergeWriter.Write(
            [new PdfMergeSource(portrait, PdfPlanKind.Pdf), new PdfMergeSource(sideways, PdfPlanKind.Pdf)],
            output);

        var pages = SharpPages(output);
        Assert.Equal(3, pages.Length);
        Assert.Equal(0, pages[0].Rotate);
        Assert.Equal(90, pages[1].Rotate);
        Assert.Equal(180, pages[2].Rotate);
    }

    [Fact]
    public void Merge_adds_an_image_as_exactly_one_page()
    {
        var pdf = CreatePdf("with-image.pdf", ("WithImage", 0, 300, 400));
        var image = Output("photo.png");
        using (var bitmap = new ImageMagick.MagickImage(ImageMagick.MagickColors.RebeccaPurple, 320, 200))
            bitmap.Write(image);
        var output = Output("MergedWithImage.pdf");

        PdfMergeWriter.Write(
            [new PdfMergeSource(pdf, PdfPlanKind.Pdf), new PdfMergeSource(image, PdfPlanKind.Image)],
            output);

        Assert.Equal(2, PageTexts(output).Length);
        var pages = SharpPages(output);
        Assert.Equal(320 * 72.0 / 96.0, pages[1].Width, 1);
        Assert.Equal(200 * 72.0 / 96.0, pages[1].Height, 1);
    }

    [Fact]
    public void Merge_normalizes_a_palette_png_that_PDFsharp_rejects()
    {
        var pdf = CreatePdf("palette-source.pdf", ("PaletteSource", 0, 300, 400));
        var image = Output("four-colour.png");

        // Four colours (including transparent) make Magick's default encoder write an indexed
        // 2-bit PNG — the exact input PDFsharp rejects with "Unsupported image format".
        using (var bitmap = new ImageMagick.MagickImage(ImageMagick.MagickColors.Transparent, 320, 200))
        {
            bitmap.Settings.FillColor = ImageMagick.MagickColors.Green;
            bitmap.Draw(new ImageMagick.Drawing.Drawables().Rectangle(20, 20, 300, 180));
            bitmap.Settings.FillColor = ImageMagick.MagickColors.White;
            bitmap.Draw(new ImageMagick.Drawing.Drawables().Rectangle(80, 60, 240, 140));
            bitmap.Settings.FillColor = ImageMagick.MagickColors.Black;
            bitmap.Draw(new ImageMagick.Drawing.Drawables().Rectangle(120, 80, 200, 120));
            File.WriteAllBytes(image, bitmap.ToByteArray(ImageMagick.MagickFormat.Png));
        }

        var header = File.ReadAllBytes(image);
        Assert.Equal(3, header[25]); // PNG colour type 3: indexed palette
        Assert.Equal(2, header[24]); // 2 bits per pixel

        var output = Output("MergedPalette.pdf");
        PdfMergeWriter.Write(
            [new PdfMergeSource(pdf, PdfPlanKind.Pdf), new PdfMergeSource(image, PdfPlanKind.Image)],
            output);

        Assert.Equal(2, PageTexts(output).Length);
        var pages = SharpPages(output);
        Assert.Equal(320 * 72.0 / 96.0, pages[1].Width, 1);
        Assert.Equal(200 * 72.0 / 96.0, pages[1].Height, 1);
    }

    [Fact]
    public void Merge_keeps_a_transparent_image_on_a_white_page()
    {
        var pdf = CreatePdf("alpha-source.pdf", ("AlphaSource", 0, 300, 400));
        var image = Output("transparent.png");
        using (var bitmap = new ImageMagick.MagickImage(ImageMagick.MagickColors.Transparent, 320, 200))
            File.WriteAllBytes(image, bitmap.ToByteArray(ImageMagick.MagickFormat.Png));
        var output = Output("MergedTransparent.pdf");

        PdfMergeWriter.Write(
            [new PdfMergeSource(pdf, PdfPlanKind.Pdf), new PdfMergeSource(image, PdfPlanKind.Image)],
            output);

        Assert.Equal(2, PageTexts(output).Length);

        // Transparency is composited on white; a dropped alpha channel would paint the page black.
        using var rendered = PDFtoImage.Conversion.ToImage(
            File.ReadAllBytes(output), 1,
            options: new PDFtoImage.RenderOptions(Dpi: 24, BackgroundColor: SkiaSharp.SKColors.White));
        Assert.Equal(SkiaSharp.SKColors.White, rendered.GetPixel(5, 5));
    }

    // ------------------------------------------------------------------ organize

    [Fact]
    public void Organize_applies_rotate_duplicate_delete_and_move_exactly()
    {
        var source = CreatePdf("pages.pdf", ("One", 0, 300, 400), ("Two", 0, 300, 400), ("Three", 0, 300, 400));
        var plan = new PdfPagePlan(source, 3);
        Assert.True(plan.Rotate(0, 90));
        Assert.True(plan.Rotate(1, 180));
        Assert.True(plan.Duplicate(1));   // One(90), Two(180), Two(180), Three
        Assert.True(plan.Delete(3));      // One(90), Two(180), Two(180)
        Assert.True(plan.Move(2, 0));     // Two(180), One(90), Two(180)
        var output = Output("organized.pdf");

        PdfPageWriter.Write(source, plan.Pages, output, sameWidth: false);

        Assert.True(File.Exists(output), "the organizer did not write its output");
        var texts = PageTexts(output);
        Assert.Equal(3, texts.Length);
        Assert.Contains("Two", texts[0], StringComparison.Ordinal);
        Assert.Contains("One", texts[1], StringComparison.Ordinal);
        Assert.Contains("Two", texts[2], StringComparison.Ordinal);

        var pages = SharpPages(output);
        Assert.Equal(3, pages.Length);
        Assert.Equal(180, pages[0].Rotate);
        Assert.Equal(90, pages[1].Rotate);
        Assert.Equal(180, pages[2].Rotate);
    }

    [Fact]
    public void Organize_same_width_scales_every_page_to_the_narrowest_width()
    {
        var source = CreatePdf("widths.pdf", ("Narrow", 0, 300, 400), ("Wide", 0, 600, 300));
        var plan = new PdfPagePlan(source, 2);
        var output = Output("same-width.pdf");

        PdfPageWriter.Write(source, plan.Pages, output, sameWidth: true);

        var pages = SharpPages(output);
        Assert.Equal(2, pages.Length);
        Assert.Equal(300, pages[0].Width, 1);
        Assert.Equal(300, pages[1].Width, 1);
        Assert.Equal(400, pages[0].Height, 1);
        Assert.Equal(150, pages[1].Height, 1);

        // Scaling the content streams must keep the text of both pages.
        var texts = PageTexts(output);
        Assert.Contains("Narrow", texts[0], StringComparison.Ordinal);
        Assert.Contains("Wide", texts[1], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ optimize

    [Fact]
    public void Optimize_never_grows_the_file()
    {
        var source = CreatePdf("compact.pdf", ("Compact", 0, 300, 400));
        var before = new FileInfo(source).Length;
        var output = Output("Compact optimized.pdf");

        PdfBackend.Optimize(source, output);

        Assert.True(File.Exists(output), "optimize did not write its output");
        var after = new FileInfo(output).Length;
        Assert.True(after <= before, $"optimize grew the file from {before} to {after} bytes");
        Assert.Contains("Compact", PageTexts(output)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Optimize_copies_the_original_through_when_recompression_would_grow()
    {
        var source = CreateMinimalPdf("minimal.pdf");
        var before = new FileInfo(source).Length;

        // Prove the fixture really is one PDFsharp would grow; otherwise this test would not cover
        // the copy-through branch if a future PDFsharp learned to write it smaller.
        var reference = Output("minimal re-saved.pdf");
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
            document.Save(reference);
        Assert.True(new FileInfo(reference).Length > before, "the fixture no longer reproduces the grow case");

        var output = Output("minimal optimized.pdf");
        PdfBackend.Optimize(source, output);

        Assert.Equal(before, new FileInfo(output).Length);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
        Assert.Single(SharpPages(output));
    }

    // ------------------------------------------------------------------ failure paths

    [Fact]
    public void Merge_fails_on_a_corrupt_pdf_without_writing_a_partial_file()
    {
        var corrupt = Output("corrupt.pdf");
        File.WriteAllText(corrupt, "this is definitely not a PDF");
        var directory = Path.Combine(_directory, "corrupt-out");
        Directory.CreateDirectory(directory);

        var error = Assert.Throws<ConversionException>(() =>
            WriteAsync(directory, "Merged", temporary =>
                PdfMergeWriter.Write([new PdfMergeSource(corrupt, PdfPlanKind.Pdf)], temporary)).GetAwaiter().GetResult());

        Assert.Contains("corrupt.pdf", error.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(File.Exists(Path.Combine(directory, "Merged.pdf")), "a partial output file was left behind");
        Assert.Empty(Directory.GetFiles(directory, "Merged*"));
    }

    [Fact]
    public void Merge_fails_on_an_encrypted_pdf_without_writing_a_partial_file()
    {
        var encrypted = CreateEncryptedPdf("locked.pdf", "SecretText");
        var valid = CreatePdf("valid.pdf", ("ValidText", 0, 300, 400));
        var directory = Path.Combine(_directory, "encrypted-out");
        Directory.CreateDirectory(directory);

        var error = Assert.Throws<ConversionException>(() =>
            WriteAsync(directory, "Merged", temporary => PdfMergeWriter.Write(
                [new PdfMergeSource(valid, PdfPlanKind.Pdf), new PdfMergeSource(encrypted, PdfPlanKind.Pdf)],
                temporary)).GetAwaiter().GetResult());

        Assert.Contains("locked.pdf", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory, "Merged.pdf")), "a partial output file was left behind");
        Assert.Empty(Directory.GetFiles(directory, "Merged*"));
    }

    [Fact]
    public void Organize_fails_on_a_corrupt_pdf_without_writing_a_partial_file()
    {
        var corrupt = Output("broken.pdf");
        File.WriteAllText(corrupt, "%PDF-1.7 but the xref is nonsense");
        var directory = Path.Combine(_directory, "organize-out");
        Directory.CreateDirectory(directory);

        var error = Assert.Throws<ConversionException>(() =>
            WriteAsync(directory, "Organized", temporary =>
                PdfPageWriter.Write(corrupt, new PdfPagePlan(corrupt, 1).Pages, temporary, sameWidth: false))
                .GetAwaiter().GetResult());

        Assert.Contains("broken.pdf", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory, "Organized*"));
    }

    [Fact]
    public void Merge_fails_on_a_read_only_folder_without_writing_a_partial_file()
    {
        var pdf = CreatePdf("read-only-source.pdf", ("ReadOnly", 0, 300, 400));
        var directory = Directory.CreateDirectory(Output("read-only-folder")).FullName;
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The test process has no Windows user identity.");
        var deny = new FileSystemAccessRule(
            identity,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Deny);
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        security.AddAccessRule(deny);
        info.SetAccessControl(security);

        try
        {
            var error = Assert.ThrowsAny<Exception>(() =>
                WriteAsync(directory, "Merged", temporary =>
                    PdfMergeWriter.Write([new PdfMergeSource(pdf, PdfPlanKind.Pdf)], temporary)).GetAwaiter().GetResult());

            // A folder the process cannot write to must surface an access error, not a silent success.
            Assert.True(
                error is UnauthorizedAccessException or IOException,
                $"unexpected error for a read-only folder: {error.GetType().Name}: {error.Message}");
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
        }
    }
}
