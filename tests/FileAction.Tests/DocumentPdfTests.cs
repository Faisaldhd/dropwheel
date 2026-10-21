using FileAction.App;
using FileAction.App.Shell;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.Tests;

/// <summary>
/// Regression: DOCX → PDF disappeared from the Convert wheel when LibreOffice was not installed.
/// Document PDF output needs LibreOffice (Pandoc alone cannot write PDF), LibreOffice is not one of the
/// helpers DropWheel downloads, and the operation's availability predicate therefore hid the slice
/// before the wheel could offer the install prompt.
/// </summary>
public class DocumentPdfTests
{
    private static FileItem Item(string name) =>
        new(Path.Combine(Path.GetTempPath(), "dropwheel-document-pdf-tests", name));

    private static ToolLocator Tools(params ExternalTool[] assumed) =>
        new ToolLocator(Path.Combine(Path.GetTempPath(), "dropwheel-document-pdf-tests")).Assuming(assumed);

    private static IReadOnlyList<Operation> Offered(FileItem item, ToolLocator tools) =>
        WheelController.Offered(WheelKind.Convert, [item], tools, new AppSettings(), WheelController.BuildOperations());

    [Theory]
    [InlineData("report.docx")]
    [InlineData("report.doc")]
    [InlineData("report.odt")]
    [InlineData("report.rtf")]
    public void Document_pdf_is_offered_when_libreoffice_is_missing(string name)
    {
        var item = Item(name);
        var tools = Tools();
        var pdf = Assert.Single(Offered(item, tools), o => o.Id == "doc.to.pdf");

        Assert.Equal("PDF", pdf.Label);
        if (tools.Has(ExternalTool.LibreOffice))
            return; // this machine has LibreOffice, so the normal path is what the wheel shows

        var needs = Assert.IsType<NeedsToolOperation>(pdf);
        Assert.Equal(new[] { ExternalTool.LibreOffice }, needs.Missing);
        Assert.DoesNotContain(ExternalTool.Pandoc, needs.Missing);
    }

    [Fact]
    public void Docx_pdf_is_a_normal_conversion_when_libreoffice_is_present()
    {
        var item = Item("report.docx");
        var tools = Tools(ExternalTool.LibreOffice);
        var pdf = Assert.Single(Offered(item, tools), o => o.Id == "doc.to.pdf");

        Assert.IsType<PerFileOperation>(pdf);
        Assert.True(pdf.IsAvailable(tools, [item]));
    }
}
