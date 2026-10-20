using System.Security.AccessControl;
using System.Security.Principal;
using FileAction.App.UI.Tools.Pdf;
using FileAction.Core;
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
