using System.Text;
using FileAction.Core;
using FileAction.Core.Backends;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// F-07 regression: a structurally valid PDF with zero pages used to produce an empty "(pages)" folder
/// and report success for both render and split. It must fail with the clear no-pages error and leave no
/// output folder.
/// </summary>
public sealed class PdfZeroPageTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _output;

    public PdfZeroPageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dropwheel-zero-pdf-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        _output = Path.Combine(_root, "output");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_output);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Render_refuses_a_zero_page_pdf_and_creates_no_folder()
    {
        var source = CreateZeroPagePdf(Path.Combine(_source, "empty.pdf"));
        Assert.Equal(0, PdfBackend.PageCount(source));

        var context = new OperationContext
        {
            Tools = new ToolLocator(),
            OutputDirectory = _output,
        };

        var exception = await Assert.ThrowsAsync<ConversionException>(
            () => PdfBackend.RenderAsync(new FileItem(source), "png", context, new Progress<double>()));

        Assert.Equal(Loc.T("error.pdf.noPages"), exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_output));
    }

    [Fact]
    public async Task Split_refuses_a_zero_page_pdf_and_creates_no_folder()
    {
        var source = CreateZeroPagePdf(Path.Combine(_source, "empty.pdf"));
        Assert.Equal(0, PdfBackend.PageCount(source));

        var context = new OperationContext
        {
            Tools = new ToolLocator(),
            OutputDirectory = _output,
        };

        var exception = await Assert.ThrowsAsync<ConversionException>(
            () => PdfBackend.SplitAsync(new FileItem(source), context, new Progress<double>()));

        Assert.Equal(Loc.T("error.pdf.noPages"), exception.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_output));
    }

    /// <summary>A minimal, valid zero-page PDF with a correct cross-reference table.</summary>
    private static string CreateZeroPagePdf(string path)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
        ];

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");

        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        File.WriteAllText(path, pdf.ToString(), new UTF8Encoding(false));
        return path;
    }
}
