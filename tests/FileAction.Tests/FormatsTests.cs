using FileAction.Core;
using FileAction.Core.Backends;
using FileAction.Core.Operations;

namespace FileAction.Tests;

/// <summary>
/// 2.3.1 format coverage: HEIC input (output is gated), BMP output, AVI/WMV video output, WMA audio
/// output and the separate EPUB group. All checks are catalog/logic checks — no file is written and no
/// backend is executed.
/// </summary>
public class FormatsTests
{
    /// <summary>A path that is never on disk; only its extension is relevant.</summary>
    private static FileItem Item(string name) => new(Path.Combine(Path.GetTempPath(), "dropwheel-formats-tests", name));

    private static ToolLocator Tools(params ExternalTool[] assumed) => new ToolLocator().Assuming(assumed);

    // ------------------------------------------------------------------ detection

    [Theory]
    [InlineData("a/photo.heic", "heic")]
    [InlineData("a/photo.HEIC", "heic")]
    [InlineData("a/photo.heif", "heic")] // alias
    [InlineData("a/clip.avi", "avi")]
    [InlineData("a/clip.AVI", "avi")]
    [InlineData("a/clip.wmv", "wmv")]
    [InlineData("a/clip.WMV", "wmv")]
    [InlineData("a/song.wma", "wma")]
    [InlineData("a/song.WMA", "wma")]
    [InlineData("a/book.epub", "epub")]
    [InlineData("a/book.EPUB", "epub")]
    public void KeyOf_detects_the_new_formats(string path, string expected) =>
        Assert.Equal(expected, Formats.KeyOf(path));

    // ------------------------------------------------------------------ groups

    [Fact]
    public void New_formats_are_in_the_expected_groups()
    {
        Assert.Contains("heic", Formats.Images);
        Assert.Contains("bmp", Formats.Images);

        Assert.Contains("avi", Formats.Video);
        Assert.Contains("wmv", Formats.Video);

        Assert.Contains("wma", Formats.Audio);

        Assert.Contains("heic", Formats.ImageOutputs);
        Assert.Contains("bmp", Formats.ImageOutputs);
        Assert.Contains("avi", Formats.VideoOutputs);
        Assert.Contains("wmv", Formats.VideoOutputs);
        Assert.Contains("wma", Formats.AudioOutputs);
    }

    [Fact]
    public void Epub_has_its_own_group_and_leaves_documents()
    {
        Assert.Contains("epub", Formats.Epub);
        Assert.Equal("epub", Assert.Single(Formats.Epub));

        // EPUB moved out of the documents group; other document formats and the EPUB output stay.
        Assert.DoesNotContain("epub", Formats.Documents);
        Assert.Contains("docx", Formats.Documents);
        Assert.Contains("epub", Formats.DocumentOutputs);

        // Pandoc still has to read EPUB for the EPUB group.
        Assert.Contains("epub", Formats.PandocInputs);

        Assert.Equal(["pdf", "docx", "txt", "md", "html"], Formats.EpubOutputs);
    }

    // ------------------------------------------------------------------ images

    [Fact]
    public void Heic_is_an_input_and_its_output_is_unavailable()
    {
        var heic = Item("photo.heic");
        Assert.Equal("heic", heic.Format);
        Assert.Equal(ImageMagick.MagickFormat.Heic, ImageBackend.ToMagickFormat("heic"));

        // Reading is supported; encoding is not (no HEVC encoder in the bundled libheif), so the
        // catalog must declare the target but never offer it.
        var all = Catalog.Build();
        var heicOp = all.Single(o => o.Id == "image.to.heic");
        Assert.True(heicOp.AppliesTo([Item("photo.png")]));
        Assert.False(heicOp.IsAvailable(new ToolLocator(), [Item("photo.png")]));

        var bmpOp = all.Single(o => o.Id == "image.to.bmp");
        Assert.True(bmpOp.AppliesTo([Item("photo.png")]));
        Assert.True(bmpOp.IsAvailable(new ToolLocator(), [Item("photo.png")]));

        var heicInput = Catalog.For(all, WheelKind.Convert, [heic], new ToolLocator());
        Assert.Contains(heicInput, o => o.Id == "image.to.jpg");
        Assert.DoesNotContain(heicInput, o => o.Id == "image.to.heic");

        var pngInput = Catalog.For(all, WheelKind.Convert, [Item("photo.png")], new ToolLocator());
        Assert.Contains(pngInput, o => o.Id == "image.to.bmp");
        Assert.DoesNotContain(pngInput, o => o.Id == "image.to.heic");
    }

    // ------------------------------------------------------------------ video

    [Fact]
    public void Video_offers_avi_and_wmv_outputs()
    {
        var all = Catalog.Build();
        var ffmpeg = Tools(ExternalTool.Ffmpeg);

        var avi = all.Single(o => o.Id == "video.to.avi");
        Assert.True(avi.AppliesTo([Item("clip.mp4")]));
        Assert.True(avi.IsAvailable(ffmpeg, [Item("clip.mp4")]));

        var wmv = all.Single(o => o.Id == "video.to.wmv");
        Assert.True(wmv.AppliesTo([Item("clip.avi")]));
        Assert.True(wmv.IsAvailable(ffmpeg, [Item("clip.avi")]));

        // AVI and WMV were already readable inputs.
        Assert.True(all.Single(o => o.Id == "video.to.mp4").AppliesTo([Item("clip.avi")]));
        Assert.True(all.Single(o => o.Id == "video.to.mp4").AppliesTo([Item("clip.wmv")]));
    }

    // ------------------------------------------------------------------ audio

    [Fact]
    public void Audio_offers_wma_output()
    {
        var all = Catalog.Build();
        var wma = all.Single(o => o.Id == "audio.to.wma");
        Assert.True(wma.AppliesTo([Item("song.wav")]));
        Assert.True(wma.IsAvailable(Tools(ExternalTool.Ffmpeg), [Item("song.wav")]));

        // WMA was already a readable input.
        Assert.True(all.Single(o => o.Id == "audio.to.mp3").AppliesTo([Item("song.wma")]));
    }

    // ------------------------------------------------------------------ EPUB group

    [Fact]
    public void Epub_group_offers_exactly_pdf_docx_txt_md_html()
    {
        var ops = Catalog.For(
            Catalog.Build(),
            WheelKind.Convert,
            [Item("book.epub")],
            Tools(ExternalTool.Pandoc, ExternalTool.LibreOffice));

        Assert.Equal(["PDF", "DOCX", "TXT", "MD", "HTML"], ops.Select(o => o.Label));
        Assert.Equal(["epub.to.pdf", "epub.to.docx", "epub.to.txt", "epub.to.md", "epub.to.html"], ops.Select(o => o.Id));
    }

    [Fact]
    public void Epub_targets_use_the_per_file_document_route()
    {
        var all = Catalog.Build();
        foreach (var id in new[] { "epub.to.pdf", "epub.to.docx", "epub.to.txt", "epub.to.md", "epub.to.html" })
        {
            // The same route helper as the documents group decides availability per input file, so the
            // EPUB operations declare no fixed tool requirement of their own and reuse DocumentAsync.
            var op = Assert.IsType<PerFileOperation>(all.Single(o => o.Id == id));
            Assert.NotNull(op.AvailableFor);
            Assert.Empty(op.Requires);
            Assert.True(op.AppliesTo([Item("book.epub")]));
        }
    }

    [Fact]
    public void Epub_no_longer_uses_the_documents_operations()
    {
        var all = Catalog.Build();
        var tools = Tools(ExternalTool.Pandoc, ExternalTool.LibreOffice);

        Assert.DoesNotContain(
            Catalog.For(all, WheelKind.Convert, [Item("book.epub")], tools),
            o => o.Id.StartsWith("doc.to.", StringComparison.Ordinal));

        // The documents group keeps working for its remaining inputs (including EPUB output).
        var documents = Catalog.For(all, WheelKind.Convert, [Item("report.docx")], tools);
        Assert.Contains(documents, o => o.Id == "doc.to.pdf");
        Assert.Contains(documents, o => o.Id == "doc.to.md");
        Assert.Contains(documents, o => o.Id == "doc.to.epub");
        Assert.DoesNotContain(documents, o => o.Id.StartsWith("epub.to.", StringComparison.Ordinal));
    }
}
