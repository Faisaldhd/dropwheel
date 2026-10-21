using System.Security.Cryptography;
using FileAction.App.UI.Tools.Meta;
using FileAction.Core;
using ImageMagick;
using Xunit.Abstractions;

namespace FileAction.Tests;

/// <summary>
/// Multi-file metadata editing: which files may share one tag form, how the common values are shown,
/// and that batch mode only counts values the user actually changed. No file, window or tool is involved.
/// </summary>
public class MetadataBatchModelTests
{
    private static FileItem Item(string name) => new(Path.Combine(Path.GetTempPath(), name));

    [Fact]
    public void Batch_kind_requires_every_file_to_share_one_supported_kind()
    {
        Assert.Equal(MetadataKind.Image, MetadataBatch.KindOf([Item("a.jpg"), Item("b.png")]));
        Assert.Equal(MetadataKind.Media, MetadataBatch.KindOf([Item("a.mp3"), Item("b.mp4")]));
        Assert.Equal(MetadataKind.Pdf, MetadataBatch.KindOf([Item("a.pdf"), Item("b.pdf")]));

        Assert.Equal(MetadataKind.None, MetadataBatch.KindOf([]));
        Assert.Equal(MetadataKind.None, MetadataBatch.KindOf([Item("a.jpg"), Item("b.pdf")]));
        Assert.Equal(MetadataKind.None, MetadataBatch.KindOf([Item("a.jpg"), Item("b.docx")]));
        Assert.Equal(MetadataKind.None, MetadataBatch.KindOf([Item("a.docx")]));
        Assert.False(MetadataBatch.Accepts([]));
        Assert.False(MetadataBatch.Accepts([Item("a.docx")]));
        Assert.True(MetadataBatch.Accepts([Item("a.jpg")]));
    }

    [Fact]
    public void Metadata_operation_is_available_for_a_batch_of_images()
    {
        var operation = new MetadataOperation();

        Assert.True(operation.IsAvailable(new ToolLocator(), [Item("a.jpg"), Item("b.png")]));
        Assert.False(operation.IsAvailable(new ToolLocator(), []));
        Assert.False(operation.IsAvailable(new ToolLocator(), [Item("a.jpg"), Item("b.pdf")]));
    }

    [Fact]
    public void Batch_preview_shows_a_value_only_when_every_file_agrees()
    {
        var one = new MetadataDocument(MetadataKind.Image,
        [
            new MetadataField(MetadataGroups.ExifKey, "Make", "Canon", MetadataEditKeys.ExifMake),
            new MetadataField(MetadataGroups.ExifKey, "Model", "EOS R6", MetadataEditKeys.ExifModel),
        ]);
        var two = new MetadataDocument(MetadataKind.Image,
        [
            new MetadataField(MetadataGroups.ExifKey, "Make", "Canon", MetadataEditKeys.ExifMake),
            new MetadataField(MetadataGroups.ExifKey, "Model", "EOS R5", MetadataEditKeys.ExifModel),
        ]);

        var fields = MetadataBatch.SharedFields([one, two]);

        Assert.Equal("Canon", fields.Single(field => field.EditKey == MetadataEditKeys.ExifMake).Value);
        Assert.Equal(string.Empty, fields.Single(field => field.EditKey == MetadataEditKeys.ExifModel).Value);
        Assert.Equal("Canon", MetadataBatch.CommonValue([one, two], MetadataEditKeys.ExifMake));
        Assert.Equal(string.Empty, MetadataBatch.CommonValue([one, two], MetadataEditKeys.ExifModel));
        Assert.Equal(string.Empty, MetadataBatch.CommonValue([one, two], "missing"));
    }

    [Fact]
    public void Batch_preview_keeps_chapters_out_and_groups_the_shared_tags()
    {
        var document = new MetadataDocument(MetadataKind.Media,
        [
            new MetadataField(MetadataGroups.FileKey, "title", "Song", MetadataEditKeys.MediaTitle),
            new MetadataField(MetadataGroups.ChaptersKey, "chapters", "0:00 Intro", MetadataEditKeys.Chapters),
            new MetadataField(MetadataGroups.FileKey, "major_brand", "isom"),
        ]);

        var fields = MetadataBatch.SharedFields([document]);

        var field = Assert.Single(fields);
        Assert.Equal(MetadataEditKeys.MediaTitle, field.EditKey);
        Assert.Equal("Song", field.Value);

        var grouped = new MetadataDocument(MetadataKind.Media, fields).Grouped("");
        var file = Assert.Single(grouped);
        Assert.Equal(MetadataGroups.FileKey, file.GroupKey);
    }

    [Fact]
    public void Batch_mode_counts_only_changed_rows_as_edits()
    {
        var edit = new MetadataEdit { ApplyOnlyChanged = true };
        edit.Seed(MetadataEditKeys.MediaTitle, "Same");
        edit.Seed(MetadataEditKeys.MediaArtist, "Artist");

        Assert.False(edit.HasEdits);

        edit.Set(MetadataEditKeys.MediaTitle, "Same");
        Assert.False(edit.HasEdits);

        edit.Set(MetadataEditKeys.MediaTitle, "New");
        Assert.True(edit.HasEdits);
        Assert.Contains(MetadataEditKeys.MediaTitle, edit.ChangedFields);

        edit.Set(MetadataEditKeys.MediaTitle, "Same");
        Assert.False(edit.HasEdits);
        Assert.Empty(edit.ChangedFields);

        edit.Remove(MetadataEditKeys.MediaArtist);
        Assert.True(edit.HasEdits);
        Assert.True(edit.IsRemoved(MetadataEditKeys.MediaArtist));
    }

    [Fact]
    public void Reset_clears_values_and_batch_bookkeeping()
    {
        var edit = new MetadataEdit { ApplyOnlyChanged = true };
        edit.Seed(MetadataEditKeys.PdfTitle, "Old");
        edit.Set(MetadataEditKeys.PdfTitle, "New");
        edit.Remove(MetadataEditKeys.PdfAuthor);
        edit.RemoveAll = true;

        edit.Reset();

        Assert.False(edit.RemoveAll);
        Assert.False(edit.HasEdits);
        Assert.Equal(string.Empty, edit.Title);
        Assert.Empty(edit.ChangedFields);
        Assert.Empty(edit.RemovedFields);
    }

    [Fact]
    public void Single_file_mode_still_counts_seeded_values_as_edits()
    {
        var edit = new MetadataEdit();
        edit.Seed(MetadataEditKeys.PdfTitle, "Existing");

        Assert.True(edit.HasEdits);
    }
}

/// <summary>
/// Drives the real batch writer against generated JPEGs: one copy per file, the same tag on every copy,
/// unique names, and byte-identical sources.
/// </summary>
[Trait("Category", "Integration")]
public class MetadataBatchFunctionTests
{
    private readonly ITestOutputHelper _output;
    private readonly ToolLocator _tools = new();

    public MetadataBatchFunctionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Metadata_batch_writes_one_copy_per_file_and_keeps_the_sources()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var first = Jpeg(dir, "one.jpg", "BeforeOne");
            var second = Jpeg(dir, "two.jpg", "BeforeTwo");
            var beforeFirst = Hash(first);
            var beforeSecond = Hash(second);
            var items = new[] { new FileItem(first), new FileItem(second) };

            var read = await MetadataRunner.ReadManyAsync(items, MetadataKind.Image, _tools, CancellationToken.None);
            Assert.Equal(2, read.Documents.Count);
            Assert.Equal(2, read.Durations.Count);

            var edit = new MetadataEdit { ApplyOnlyChanged = true };
            edit.Set(MetadataEditKeys.ExifMake, "BatchCam");

            var outputs = await MetadataRunner.WriteManyAsync(
                items, MetadataKind.Image, edit, read.Durations, null, _tools, CancellationToken.None, new Progress<double>());

            Assert.Equal(2, outputs.Count);
            Assert.Equal("one Edited metadata.jpg", Path.GetFileName(outputs[0]));
            Assert.Equal("two Edited metadata.jpg", Path.GetFileName(outputs[1]));
            foreach (var output in outputs)
            {
                _output.WriteLine(output);
                using var image = new MagickImage(output);
                var exif = image.GetExifProfile();
                Assert.NotNull(exif);
                Assert.Equal("BatchCam", exif!.GetValue(ExifTag.Make)?.GetValue() as string);
            }

            Assert.Equal(beforeFirst, Hash(first));
            Assert.Equal(beforeSecond, Hash(second));
            Assert.Empty(Directory.GetFiles(dir, "*.part.*"));
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    [Fact]
    public async Task Metadata_batch_remove_all_strips_every_copy()
    {
        var dir = OutputPaths.TempDirectory();
        try
        {
            var first = Jpeg(dir, "one.jpg", "BeforeOne");
            var second = Jpeg(dir, "two.jpg", "BeforeTwo");
            var items = new[] { new FileItem(first), new FileItem(second) };
            var read = await MetadataRunner.ReadManyAsync(items, MetadataKind.Image, _tools, CancellationToken.None);

            var outputs = await MetadataRunner.WriteManyAsync(
                items,
                MetadataKind.Image,
                new MetadataEdit { ApplyOnlyChanged = true, RemoveAll = true },
                read.Durations,
                null,
                _tools,
                CancellationToken.None,
                new Progress<double>());

            Assert.Equal(2, outputs.Count);
            foreach (var output in outputs)
            {
                using var image = new MagickImage(output);
                var exif = image.GetExifProfile();
                Assert.True(exif is null || exif.GetValue(ExifTag.Make) is null, $"Remove All kept the camera make in {output}");
            }
        }
        finally
        {
            OutputPaths.TryDelete(dir);
        }
    }

    private static string Jpeg(string directory, string name, string make)
    {
        var path = Path.Combine(directory, name);
        using var image = new MagickImage(MagickColors.SkyBlue, 320, 240);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, make);
        image.SetProfile(exif);
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
