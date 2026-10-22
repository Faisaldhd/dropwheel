using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Lock;
using FileAction.Core.Operations;
using ImageMagick;
using Xunit;
using static FileAction.Core.Loc;
using IDataObject = System.Windows.IDataObject;

namespace FileAction.Tests;

/// <summary>
/// The drag-and-drop pipeline as far as it can be driven without a desktop session: what the drop reader
/// accepts, which folder the drop resolves to, which tool a drop of N items offers, and what the real
/// write does to folders A and B (copy-style and move-style), including the same-folder case, name
/// collisions, cancellation and partial output.
///
/// Every test creates its own two-folder work area under <c>&lt;repo&gt;\_audit-ci\dragdrop\&lt;guid&gt;</c>
/// and deletes it again, so nothing is ever written to %TEMP%, Documents, Desktop or the profile root.
/// No window, no UI thread, no network and no external program is used: the image work is Magick.NET
/// in-process, the archive work is SharpCompress/ZipArchive in-process and the move-style case is the
/// shipped Lock pipeline with its cheap test iteration count.
/// </summary>
public sealed class DragDropTests : IDisposable
{
    private readonly string _root;
    private readonly string _a;
    private readonly string _b;

    public DragDropTests()
    {
        _root = Path.Combine(ScratchRoot(), "dragdrop", Guid.NewGuid().ToString("N")[..12]);
        var work = Path.Combine(_root, "work");
        _a = Path.Combine(work, "A");
        _b = Path.Combine(work, "B");
        Directory.CreateDirectory(_a);
        Directory.CreateDirectory(_b);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover scratch folder must never fail an otherwise green test.
        }
    }

    // ------------------------------------------------------------------ 1. A -> B, copy-style

    [Fact]
    public async Task Between_folders_copy_style_writes_into_B_and_leaves_A_byte_identical()
    {
        var source = CreatePng(_a, "photo.png", 120, 80, MagickColors.Red);
        var before = Sha256(source);

        // The wheel path: drop a file from A while the cursor is over B.
        var label = DropTargetLabels.Choose(
            OutputTarget.SourceFolder(source), _b, null, Directory.Exists, OutputTarget.CanWrite);
        Assert.Equal(DropTargetLabelKind.Folder, label.Kind);
        Assert.Equal(_b, label.Folder);

        var resolved = OutputTarget.Resolve(source, label.Folder, Directory.Exists, OutputTarget.CanWrite);
        Assert.Equal(OutputTargetStatus.UseDetectedFolder, resolved.Status);
        Assert.Equal(_b, resolved.Directory);

        // JobRunner passes exactly resolved.Directory as OperationContext.OutputDirectory.
        var result = await Operation("image.rotate.right")
            .ExecuteAsync([new FileItem(source)], Context(resolved.Directory));

        var output = Assert.Single(result.Outputs);
        Assert.Equal(_b, Path.GetDirectoryName(output));
        Assert.True(File.Exists(output));
        Assert.Single(Directory.GetFiles(_b));

        Assert.True(File.Exists(source));
        Assert.Equal(before, Sha256(source));
        Assert.Single(Directory.GetFiles(_a));

        using var rendered = new MagickImage(output);
        Assert.Equal(80u, rendered.Width);
        Assert.Equal(120u, rendered.Height);
    }

    // ------------------------------------------------------------------ 2. A -> B, move-style

    [Fact]
    public void Between_folders_move_style_relocates_the_only_copy_into_B()
    {
        var source = Path.Combine(_a, "note.txt");
        File.WriteAllText(source, "keep me");
        var before = Sha256(source);

        // The floating Lock tool resolves its output folder through ToolOutputFolder.For, which is the
        // same rule the job path uses: the usable drop folder wins, otherwise next to the source.
        Assert.Equal(_b, ToolOutputFolder.For(new FileItem(source), new DropTargets(_b, null)));
        Assert.Equal(_a, ToolOutputFolder.For(new FileItem(source), new DropTargets(_a, null)));

        var service = NewLockService();
        var outcome = service.Protect(source, _b, Credentials());

        Assert.True(outcome.SourceRemoved);
        Assert.Null(outcome.LeftoverSource);
        Assert.Equal(_b, Path.GetDirectoryName(outcome.OutputPath));
        Assert.True(File.Exists(outcome.OutputPath));
        Assert.Equal("note.txt.dwenc", Path.GetFileName(outcome.OutputPath));

        // Move-style: the plaintext is gone from A, and A is empty; B has exactly the container.
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFileSystemEntries(_a));
        Assert.Single(Directory.GetFileSystemEntries(_b));
        Assert.Empty(Directory.GetFiles(_b, "*.tmp*"));

        var unprotect = service.Unprotect(outcome.OutputPath, "file-password-1", recycleContainer: false);
        Assert.Equal(before, Sha256(unprotect.RestoredPath));
        Assert.Equal("keep me", File.ReadAllText(unprotect.RestoredPath));
    }

    // ------------------------------------------------------------------ 3. A -> A, same folder

    [Fact]
    public async Task Same_folder_drop_writes_next_to_the_file_and_never_overwrites_or_loses_data()
    {
        var source = CreatePng(_a, "photo.png", 120, 80, MagickColors.Green);
        var before = Sha256(source);

        // Dropping onto the folder the file already lives in is "next to the file", not a destination.
        var same = OutputTarget.Resolve(source, _a, Directory.Exists, OutputTarget.CanWrite);
        Assert.Equal(OutputTargetStatus.SameAsSource, same.Status);
        Assert.Null(same.Directory);

        var operation = Operation("image.rotate.right");
        var first = Assert.Single((await operation.ExecuteAsync([new FileItem(source)], Context(null))).Outputs);
        var firstBytes = Sha256(first);
        var second = Assert.Single((await operation.ExecuteAsync([new FileItem(source)], Context(null))).Outputs);

        Assert.Equal(_a, Path.GetDirectoryName(first));
        Assert.Equal(_a, Path.GetDirectoryName(second));
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(firstBytes, Sha256(first));
        Assert.Equal(before, Sha256(source));
        Assert.Equal(3, Directory.GetFiles(_a).Length); // source + two results, nothing lost, nothing overwritten
    }

    [Fact]
    public async Task Same_folder_name_collision_gets_a_new_name_and_keeps_the_existing_file()
    {
        var source = CreatePng(_a, "photo.png", 120, 80, MagickColors.Blue);
        var beforeSource = Sha256(source);

        // The exact first-choice output name already exists with unrelated content.
        var takenName = $"photo ({T("catalog.image.rotate.suffix")}).png";
        var taken = Path.Combine(_a, takenName);
        File.WriteAllBytes(taken, [1, 2, 3, 4]);
        var beforeTaken = Sha256(taken);

        var output = Assert.Single((await Operation("image.rotate.right")
            .ExecuteAsync([new FileItem(source)], Context(null))).Outputs);

        Assert.Equal($"photo ({T("catalog.image.rotate.suffix")}) (2).png", Path.GetFileName(output));
        Assert.Equal(beforeTaken, Sha256(taken));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(taken));
        Assert.Equal(beforeSource, Sha256(source));
        Assert.Equal(3, Directory.GetFiles(_a).Length);
    }

    // ------------------------------------------------------------------ 4. several files, mixed types, a folder

    [Fact]
    public async Task Several_files_and_a_dropped_folder_pack_into_one_zip_in_the_drop_folder()
    {
        var photo = CreatePng(_a, "photo.png", 40, 30, MagickColors.Red);
        var notes = Path.Combine(_a, "notes.txt");
        File.WriteAllText(notes, "hello");
        var folder = Path.Combine(_a, "bundle");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "inner.txt"), "inner");

        var items = new List<FileItem> { new(photo), new(notes), new(folder) };
        var operation = Operation("pack.zip");
        Assert.True(operation.AppliesTo(items));

        var result = await operation.ExecuteAsync(items, Context(_b));
        var archive = Assert.Single(result.Outputs);

        Assert.Equal(_b, Path.GetDirectoryName(archive));
        Assert.EndsWith(".zip", archive, StringComparison.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(archive);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "bundle/inner.txt", "notes.txt", "photo.png" }, names);
        Assert.Equal("hello", ReadEntry(zip, "notes.txt"));
        Assert.Equal("inner", ReadEntry(zip, "bundle/inner.txt"));
        Assert.Equal("hello", File.ReadAllText(notes)); // sources are only read
        Assert.Single(Directory.GetFiles(_b));
    }

    // ------------------------------------------------------------------ 5. tool dispatch by count and type

    public static TheoryData<string, string[], string[]> DispatchByDrop =>
        new()
        {
            { "one image", ["tools.crop", "tools.editphotos"], ["tools.collage", "tools.videotrim"] },
            { "two images", ["tools.collage"], ["tools.crop", "tools.editphotos"] },
            { "one video", ["tools.videotrim", "tools.videocrop"], ["tools.crop", "tools.collage"] },
            { "one audio file", ["tools.audiotrim"], ["tools.videotrim", "tools.crop"] },
            { "one folder", ["pack.zip"], ["tools.crop", "tools.compress", "tools.collage"] },
            { "image and video together", ["pack.zip"], ["tools.crop", "tools.videotrim", "tools.collage"] },
        };

    [Theory]
    [MemberData(nameof(DispatchByDrop))]
    public void Drop_shape_offers_exactly_the_tools_that_accept_it(string shape, string[] expected, string[] refused)
    {
        var items = ItemsFor(shape);
        var offered = Offered(items);

        foreach (var id in expected)
            Assert.Contains(id, offered);
        foreach (var id in refused)
            Assert.DoesNotContain(id, offered);
    }

    [Theory]
    [MemberData(nameof(DropShapes))]
    public void A_tool_is_on_the_wheel_only_when_its_registered_window_accepts_the_same_drop(string shape)
    {
        var items = ItemsFor(shape);
        var offered = Offered(items).ToHashSet(StringComparer.Ordinal);

        foreach (var spec in ToolRegistry.All.Where(s => offered.Contains(s.Id)))
            Assert.True(spec.Applies(items), $"{spec.Id} is offered for '{shape}' but its window refuses the drop");
    }

    public static TheoryData<string> DropShapes => new()
    {
        "one image",
        "two images",
        "one video",
        "one audio file",
        "one folder",
        "image and video together",
    };

    private List<FileItem> ItemsFor(string shape) => shape switch
    {
        "one image" => Items(CreatePng(_a, "one.png", 40, 30, MagickColors.Red)),
        "two images" => Items(CreatePng(_a, "one.png", 40, 30, MagickColors.Red), CreatePng(_a, "two.png", 30, 40, MagickColors.Red)),
        "one video" => Items(WriteFile(_a, "clip.mp4", new byte[] { 0, 0, 0, 0x18 })),
        "one audio file" => Items(WriteFile(_a, "song.mp3", new byte[] { 0x49, 0x44, 0x33 })),
        "one folder" => Items(Directory.CreateDirectory(Path.Combine(_a, "stuff")).FullName),
        "image and video together" => Items(
            CreatePng(_a, "one.png", 40, 30, MagickColors.Red),
            WriteFile(_a, "clip.mp4", new byte[] { 0, 0, 0, 0x18 })),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown drop shape"),
    };

    [Fact]
    public void An_empty_drop_offers_nothing()
    {
        Assert.Empty(Offered([]));
    }

    [Fact]
    public void A_tool_that_accepts_one_file_is_dispatched_by_id_and_refuses_a_multi_file_drop()
    {
        var one = Items(CreatePng(_a, "one.png", 40, 30, MagickColors.Red));
        var two = Items(
            CreatePng(_a, "one.png", 40, 30, MagickColors.Red),
            CreatePng(_a, "two.png", 30, 40, MagickColors.Red));

        var single = ToolRegistry.For("tools.editphotos", one);
        Assert.NotNull(single);
        Assert.Equal("tools.editphotos", single!.Id);
        Assert.True(Operation("tools.editphotos").AppliesTo(one));
        Assert.False(Operation("tools.editphotos").AppliesTo(two));
        Assert.Null(ToolRegistry.For("tools.editphotos", two));
        Assert.Null(ToolRegistry.For("tools.crop", two));

        Assert.Null(ToolRegistry.For("tools.collage", one));
        var collage = ToolRegistry.For("tools.collage", two);
        Assert.NotNull(collage);
        Assert.Equal("tools.collage", collage!.Id);
    }

    [Fact]
    public void Every_registered_tool_window_is_reachable_from_the_wheel_by_its_own_id()
    {
        var registered = ToolRegistry.All.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(26, registered.Length); // 27 minus the removed Unlock password tool
        Assert.Equal(registered.Length, registered.Distinct(StringComparer.Ordinal).Count());

        var wheel = WheelController.BuildOperations().Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var missing = registered.Where(id => !wheel.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        // 4f23c9c wired Annotate and Redact into the wheel, so every registered window must now be offered
        // by its own id; a registered-but-unwired tool is exactly the regression this asserts against.
        Assert.Empty(missing);
    }

    // ------------------------------------------------------------------ 6. cancel / close mid-flow

    [Fact]
    public async Task Cancel_mid_flow_leaves_the_source_intact_and_writes_no_partial_output()
    {
        var source = CreatePng(_a, "photo.png", 120, 80, MagickColors.Red);
        var before = Sha256(source);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The job card's Cancel button cancels exactly this token (JobRunner passes cts.Token).
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Operation("image.rotate.right").ExecuteAsync([new FileItem(source)], Context(_b, cts.Token)));

        Assert.Equal(before, Sha256(source));
        Assert.Empty(Directory.GetFiles(_b));
    }

    [Fact]
    public async Task Cancel_after_half_a_file_leaves_no_output_and_no_part_file()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OutputPaths.ProduceAsync(_b, "half", "png", async temp =>
        {
            await File.WriteAllBytesAsync(temp, new byte[2048]);
            throw new OperationCanceledException();
        }));

        Assert.Empty(Directory.GetFiles(_b));
    }

    // ------------------------------------------------------------------ 7. non-file drops

    [Fact]
    public void A_non_file_drop_is_rejected_without_throwing_or_writing_anything()
    {
        Assert.Null(DropContent.Read(new StubData { Text = "some text the user dragged from Notepad" }, materialize: true));
        Assert.Null(DropContent.Read(new StubData { Text = "ftp://example.com/file.zip" }, materialize: true));
        Assert.Null(DropContent.Read(new StubData(), materialize: true));

        Assert.Empty(Directory.GetFiles(_a));
        Assert.Empty(Directory.GetFiles(_b));
    }

    [Fact]
    public void A_web_link_is_recognised_as_one_item_and_a_drag_preview_never_downloads()
    {
        var preview = DropContent.Read(new StubData { Text = "https://example.com/picture.png" }, materialize: false);

        Assert.NotNull(preview);
        var item = Assert.Single(preview!.Items);
        Assert.Equal("picture.png", item.Name);
        Assert.False(File.Exists(item.Path));

        // A drag preview carries no download work at all (`DropReader.Download`, materialize:false); the
        // lazy `Prepare` is attached only on the drop (materialize:true), and that branch creates
        // Downloads\DropWheel and needs the network, so it is deliberately not exercised here: a test
        // must not write outside the repository. The attachment is source-verified
        // (DropReader.cs:223-239).
        Assert.Null(preview.Prepare);
    }

    [Fact]
    public void A_file_drop_arrives_as_paths_and_only_copies_when_the_file_is_materialised()
    {
        var source = CreatePng(_a, "photo.png", 40, 30, MagickColors.Red);

        var preview = DropContent.Read(new StubData { Files = [source] }, materialize: false);
        Assert.NotNull(preview);
        Assert.Equal(source, Assert.Single(preview!.Items).Path);
        Assert.Null(preview.Prepare);

        var dropped = DropContent.Read(new StubData { Files = [source] }, materialize: true);
        Assert.NotNull(dropped);
        Assert.Equal(source, Assert.Single(dropped!.Items).Path);
        Assert.Equal(Sha256(source), Sha256(Assert.Single(Directory.GetFiles(_a))));
    }

    // ------------------------------------------------------------------ helpers

    private static Operation Operation(string id) =>
        WheelController.BuildOperations().Single(o => o.Id == id);

    private static List<FileItem> Items(params string[] paths) => paths.Select(p => new FileItem(p)).ToList();

    /// <summary>The wheel-agnostic set of operations whose AppliesTo accepts this drop.</summary>
    private static string[] Offered(IReadOnlyList<FileItem> items) =>
        WheelController.BuildOperations()
            .Where(o => o.AppliesTo(items))
            .Select(o => o.Id)
            .ToArray();

    private static OperationContext Context(string? outputDirectory, CancellationToken token = default) =>
        new()
        {
            Tools = new ToolLocator(),
            OutputDirectory = outputDirectory,
            CancellationToken = token,
        };

    private static LockService NewLockService() =>
        new(new LockEnvironment([], []), NullLockHost.Instance, minimumIterations: 1000, newSlotIterations: 1000);

    private static LockCredentials Credentials() => new()
    {
        FilePassword = "file-password-1",
        RescuerPassword = "rescuer-password-1",
        RescuerKey = RescuerKey.Generate(),
        FilePasswordIterations = 1000,
        RescuerPasswordIterations = 1000,
    };

    private string CreatePng(string folder, string name, int width, int height, MagickColor color)
    {
        var path = Path.Combine(folder, name);
        using var image = new MagickImage(color, (uint)width, (uint)height);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private static string WriteFile(string folder, string name, byte[] bytes)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>
    /// Where the per-test two-folder work area lives: on a developer machine
    /// <c>&lt;repo&gt;\_local\samples\afunc</c> (git-ignored, never committed); on CI a private
    /// <c>&lt;repo&gt;\_audit-ci</c> folder inside the job workspace. Each test deletes its own folder.
    /// </summary>
    private static string ScratchRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? repo = null;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
            {
                repo = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }
        var root = repo ?? AppContext.BaseDirectory;
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
            ? Path.Combine(root, "_local", "samples", "afunc")
            : Path.Combine(root, "_audit-ci");
    }

    /// <summary>Minimal WPF data object so the drop reader can be driven without a shell.</summary>
    private sealed class StubData : IDataObject
    {
        public string? Text { get; init; }

        public string[]? Files { get; init; }

        public object? GetData(string format)
        {
            if (format == DataFormats.FileDrop)
                return Files;
            if (format == DataFormats.UnicodeText)
                return Text;
            return null;
        }

        public object? GetData(Type format) => null;

        public object? GetData(string format, bool autoConvert) => GetData(format);

        public bool GetDataPresent(string format) => GetData(format) is not null;

        public bool GetDataPresent(Type format) => false;

        public bool GetDataPresent(string format, bool autoConvert) => GetDataPresent(format);

        public string[] GetFormats() => [DataFormats.UnicodeText];

        public string[] GetFormats(bool autoConvert) => GetFormats();

        public void SetData(object data)
        {
        }

        public void SetData(string format, object data)
        {
        }

        public void SetData(Type format, object data)
        {
        }

        public void SetData(string format, object data, bool autoConvert)
        {
        }
    }
}
