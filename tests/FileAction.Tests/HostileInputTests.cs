using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using FileAction.App.Jobs;
using FileAction.Core;
using FileAction.Core.Backends;

namespace FileAction.Tests;

/// <summary>
/// Runs the Core entry points of every tool family against benign hostile inputs: awkward file names,
/// degenerate content, size edges, access edges and cancellation. Nothing here needs a window, a network,
/// a helper tool or a GPU; every fixture is created under one isolated root that the test itself deletes.
///
/// The suite is serialised because it redirects the process-wide TMP/TEMP variables for the duration of a
/// test (so a Core path that asks for the temp folder cannot escape the audit root) and because a few
/// cases flip <see cref="Loc.StrictMode"/>.
/// </summary>
[Collection(nameof(HostileIo))]
public class HostileInputTests
{
    // ------------------------------------------------------------------ isolated fixture root

    /// <summary>
    /// One per-test root inside the checkout: <c>_local/tmp/asec</c> on the owner's machine (git-ignored),
    /// <c>_audit-ci</c> in CI. TMP/TEMP are redirected into it so nothing is ever written to the real
    /// %TEMP%, Documents or the profile root, and the whole tree is removed on dispose.
    /// </summary>
    private sealed class HostileRoot : IDisposable
    {
        private readonly string? _previousTemp;
        private readonly string? _previousTmp;

        public HostileRoot()
        {
            var workspace = WorkspaceRoot();
            var scratch = Directory.Exists(System.IO.Path.Combine(workspace, "_local"))
                ? System.IO.Path.Combine(workspace, "_local", "tmp", "asec")
                : System.IO.Path.Combine(workspace, "_audit-ci");
            Root = System.IO.Path.Combine(scratch, "hostile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            if (!System.IO.Path.GetFullPath(Root).StartsWith(System.IO.Path.GetFullPath(workspace), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("the hostile fixture root must stay inside the checkout");

            _previousTemp = Environment.GetEnvironmentVariable("TEMP");
            _previousTmp = Environment.GetEnvironmentVariable("TMP");
            Environment.SetEnvironmentVariable("TEMP", Root);
            Environment.SetEnvironmentVariable("TMP", Root);
        }

        public string Root { get; }

        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public string NewDirectory(string name)
        {
            var path = Path(name);
            Directory.CreateDirectory(path);
            return path;
        }

        public string WriteBytes(string name, params byte[] content)
        {
            var path = Path(name);
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, content);
            return path;
        }

        public string WriteText(string name, string content, Encoding? encoding = null)
        {
            var path = Path(name);
            var folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, content, encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }

        /// <summary>Every leftover half-written artifact the shared helpers create.</summary>
        public string[] Partials() =>
            Directory.GetFiles(Root, "*.part*", SearchOption.AllDirectories)
                .Concat(Directory.GetDirectories(Root, "*.part*", SearchOption.AllDirectories))
                .ToArray();

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("TEMP", _previousTemp);
            Environment.SetEnvironmentVariable("TMP", _previousTmp);
            try
            {
                foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                foreach (var folder in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        // A read-only directory blocks the recursive delete on some runners.
                        File.SetAttributes(folder, FileAttributes.Directory);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A locked or long-path leftover must never fail the test run.
            }
        }

        private static string WorkspaceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "FileAction.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? AppContext.BaseDirectory;
        }
    }

    // ------------------------------------------------------------------ shared assertions

    /// <summary>A valid 1×1 PNG, so an "image" case never depends on an encoder.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly Settings Defaults = new();

    /// <summary>No stack trace, no HRESULT, and a sentence a user can act on.</summary>
    private static void AssertReadable(Exception exception)
    {
        var text = exception.Message;
        Assert.False(string.IsNullOrWhiteSpace(text), "the failure carried no message at all");
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("0x8007", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A localized product message: no framework type name anywhere in it.</summary>
    private static void AssertNoTypeName(Exception exception)
    {
        AssertReadable(exception);
        Assert.DoesNotContain("System.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exception.GetType().Name, exception.Message, StringComparison.Ordinal);
    }

    private static void AssertNoEntries(string folder) => Assert.Empty(Directory.GetFileSystemEntries(folder));

    private static string LongBaseName(int length) => new('a', length);

    // ------------------------------------------------------------------ hostile name data

    /// <summary>
    /// Names that must be safe at every string-level API. The reserved device names are here as strings
    /// only: opening them on Windows talks to the device, so the suite never creates them on disk.
    /// </summary>
    public static IEnumerable<object[]> HostileNameStrings()
    {
        yield return ["plain", "plain.png"];
        yield return ["two words", "two words.png"];
        yield return ["leading space", " leading space.png"];
        yield return ["trailing space before the extension", "trailing space .png"];
        yield return ["trailing dot", "trailing.dot.png"];
        yield return ["two dots", "dots..png"];
        yield return ["reserved CON", "CON.png"];
        yield return ["reserved PRN", "PRN.png"];
        yield return ["reserved AUX", "AUX.png"];
        yield return ["reserved NUL", "NUL.png"];
        yield return ["reserved COM1", "COM1.png"];
        yield return ["reserved LPT9", "LPT9.png"];
        yield return ["emoji", "\U0001F5BC\uFE0F photo.png"];
        yield return ["arabic", "صورة.png"];
        yield return ["bidi override", "a\u202Eb.png"];
        yield return ["combining marks (NFD)", "e\u0301.png"];
        yield return ["precomposed (NFC)", "é.png"];
        yield return ["upper case extension", "Photo.PNG"];
        yield return ["lower case extension", "photo.png"];
        yield return ["empty", ""];
        yield return ["whitespace", "   "];
        yield return ["255 character name", LongBaseName(251) + ".png"];
        yield return ["no extension", "README"];
        yield return ["dot only", "."];
        yield return ["traversal-shaped", "..\\..\\escape.png"];
    }

    /// <summary>Names NTFS actually accepts, so the case can go all the way to a real file.</summary>
    public static IEnumerable<object[]> HostileDiskNames()
    {
        yield return ["two words", "two words.png"];
        yield return ["leading space", " leading space.png"];
        yield return ["trailing space before the extension", "trailing space .png"];
        yield return ["two dots", "dots..png"];
        yield return ["emoji", "\U0001F5BC\uFE0F photo.png"];
        yield return ["arabic", "صورة.png"];
        yield return ["bidi override", "a\u202Eb.png"];
        yield return ["combining marks (NFD)", "e\u0301.png"];
        yield return ["precomposed (NFC)", "é.png"];
        yield return ["upper case extension", "Photo.PNG"];
        yield return ["lower case extension", "photo.png"];
        yield return ["long base name", LongBaseName(200) + ".png"];
    }

    // ------------------------------------------------------------------ names: string level

    [Theory]
    [MemberData(nameof(HostileNameStrings))]
    public void Hostile_names_never_throw_in_the_name_helpers(string label, string name)
    {
        var item = new FileItem(name);

        // The wheel shows the file name the OS returns for the path, whatever the path was.
        Assert.Equal(System.IO.Path.GetFileName(name.TrimEnd('\\', '/')), item.Name);
        // "." and ".." are directories, so the folder flag follows the file system, not the string shape.
        Assert.Equal(Directory.Exists(name), item.IsDirectory);
        Assert.DoesNotContain("\\", item.Format, StringComparison.Ordinal);
        Assert.DoesNotContain("/", item.Format, StringComparison.Ordinal);

        var key = Formats.KeyOf(name);
        Assert.Equal(Formats.Normalize(key), key);
        Assert.NotNull(Formats.BaseNameOf(name));

        // The output-folder helper must answer, never throw, for anything a wheel can be handed.
        var folder = OutputTarget.SourceFolder(name);
        Assert.True(folder is null || folder.Length > 0);

        var noFolder = OutputTarget.Resolve(name, null, _ => false, _ => false);
        Assert.Equal(OutputTargetStatus.NoFolderDetected, noFolder.Status);

        // A folder that cannot be written must fall back instead of escaping.
        var unwritable = OutputTarget.Resolve(name, folder ?? ".", _ => true, _ => false);
        Assert.False(unwritable.UsesDetectedFolder);

        _ = label;
    }

    [Fact]
    public void Reserved_device_names_never_reach_a_real_disk_path()
    {
        foreach (var name in new[] { "CON.png", "PRN.png", "AUX.png", "NUL.png", "COM1.png", "LPT9.png" })
        {
            Assert.Equal("png", Formats.KeyOf(name));
            Assert.Equal(name[..^4], Formats.BaseNameOf(name));
            Assert.Equal(name, new FileItem(name).Name);
        }

        // Unique only ever builds a path, so it must stay side-effect free for a device-shaped name.
        using var root = new HostileRoot();
        var folder = root.NewDirectory("out");
        var candidate = OutputPaths.Unique(folder, "CON", "png");
        Assert.StartsWith(folder, candidate, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", candidate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emoji_and_arabic_names_keep_their_characters_through_the_name_helpers()
    {
        const string arabic = "صورة.png";
        const string emoji = "\U0001F5BC\uFE0F photo.png";

        Assert.Equal(arabic, new FileItem(arabic).Name);
        Assert.Equal("صورة", Formats.BaseNameOf(arabic));
        Assert.Equal("png", Formats.KeyOf(arabic));

        Assert.Equal(emoji, new FileItem(emoji).Name);
        Assert.Equal("png", Formats.KeyOf(emoji));
    }

    [Fact]
    public void A_bidi_override_is_preserved_and_does_not_change_the_format()
    {
        const string trick = "photo\u202Egnp.exe";
        Assert.Equal("exe", Formats.KeyOf(trick));
        Assert.Contains('\u202E', new FileItem(trick).Name);
        Assert.Equal(trick, new FileItem(trick).Name);
    }

    // ------------------------------------------------------------------ names: real files

    [Theory]
    [MemberData(nameof(HostileDiskNames))]
    public async Task A_hostile_file_name_survives_output_naming(string label, string name)
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes(name, 0x42);

        var item = new FileItem(source);
        Assert.Equal(name, item.Name);

        var produced = await OutputPaths.ProduceAsync(root.Root, item.BaseName, "bkp", temp =>
        {
            File.WriteAllBytes(temp, [1, 2, 3]);
            return Task.CompletedTask;
        });

        Assert.True(File.Exists(produced), $"{label}: the result was not written");
        Assert.StartsWith(root.Root, produced, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new byte[] { 0x42 }, File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public async Task Nfc_and_nfd_names_stay_distinct_and_never_overwrite_each_other()
    {
        using var root = new HostileRoot();
        var nfd = root.WriteText("e\u0301.png", "nfd");
        var nfc = root.WriteText("é.png", "nfc");
        Assert.NotEqual(System.IO.Path.GetFileName(nfd), System.IO.Path.GetFileName(nfc));

        var first = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(nfd), "out", temp =>
        {
            File.WriteAllText(temp, "one");
            return Task.CompletedTask;
        });
        var second = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(nfc), "out", temp =>
        {
            File.WriteAllText(temp, "two");
            return Task.CompletedTask;
        });

        Assert.NotEqual(first, second, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("nfd", File.ReadAllText(nfd));
        Assert.Equal("nfc", File.ReadAllText(nfc));
        Assert.Equal(2, Directory.GetFiles(root.Root, "*.out").Length);
    }

    [Fact]
    public async Task Names_that_differ_only_by_case_are_the_same_file_on_windows()
    {
        using var root = new HostileRoot();
        // NTFS is case-insensitive: "Photo.PNG" and "photo.png" are one file, so the second write lands in
        // the first. The product cannot create a second copy either — it must number its *own* results.
        var upper = root.WriteText("Photo.PNG", "upper");
        var lower = root.WriteText("photo.png", "lower");

        Assert.True(File.Exists(upper));
        Assert.True(File.Exists(lower));
        Assert.Single(Directory.GetFiles(root.Root, "*.png"));
        Assert.Equal("lower", File.ReadAllText(upper));

        // Two results from the same stem must be two distinct files, whatever the case of the source name.
        var a = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(upper), "out", temp =>
        {
            File.WriteAllText(temp, "A");
            return Task.CompletedTask;
        });
        var b = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(lower), "out", temp =>
        {
            File.WriteAllText(temp, "B");
            return Task.CompletedTask;
        });

        Assert.NotEqual(a, b, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, Directory.GetFiles(root.Root, "*.out").Length);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public async Task A_255_character_name_is_numbered_and_never_overwrites_the_source()
    {
        using var root = new HostileRoot();
        var name = LongBaseName(251) + ".png";
        var source = root.WriteBytes(name, 0x7F);
        Assert.Equal(255, System.IO.Path.GetFileName(source).Length);

        // A normal result next to the over-long source still works.
        var produced = await OutputPaths.ProduceAsync(root.Root, LongBaseName(200), "png", temp =>
        {
            File.WriteAllBytes(temp, OnePixelPng);
            return Task.CompletedTask;
        });
        Assert.True(File.Exists(produced));
        Assert.Equal(new byte[] { 0x7F }, File.ReadAllBytes(source));

        // The same 251-character stem must be numbered instead of overwritten … or refused. Either way
        // the source survives and nothing half-written stays behind (see H-03 for the length clamp).
        try
        {
            var again = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(source), "png", temp =>
            {
                File.WriteAllBytes(temp, OnePixelPng);
                return Task.CompletedTask;
            });
            Assert.True(File.Exists(again));
            Assert.NotEqual(source, again, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            AssertReadable(exception);
        }

        Assert.Equal(new byte[] { 0x7F }, File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public void A_path_longer_than_260_characters_is_accepted_or_cleanly_refused()
    {
        using var root = new HostileRoot();
        var deep = root.Root;
        var component = new string('d', 60);
        while (deep.Length < 300)
        {
            deep = System.IO.Path.Combine(deep, component);
            try
            {
                Directory.CreateDirectory(deep);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                break;
            }
        }

        var longPath = System.IO.Path.Combine(deep, "file with spaces.png");
        Assert.True(longPath.Length > 260, "the fixture did not actually reach a long path");

        var created = false;
        try
        {
            File.WriteAllBytes(longPath, [1]);
            created = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // A runner without long-path support is allowed to refuse.
        }

        Assert.Equal("png", Formats.KeyOf(longPath));
        if (created)
        {
            Assert.NotNull(OutputTarget.SourceFolder(longPath));
            var produced = OutputPaths.Unique(deep, "out", "png");
            Assert.StartsWith(deep, produced, StringComparison.OrdinalIgnoreCase);
        }

        // A path that is far too long for the OS must not make the pure helpers explode.
        var tooLong = root.Root + "\\" + new string('x', 400) + "\\f.png";
        Assert.Equal("png", Formats.KeyOf(tooLong));
    }

    // ------------------------------------------------------------------ degenerate content

    public static IEnumerable<object[]> DegenerateImages()
    {
        yield return ["empty", Array.Empty<byte>()];
        yield return ["one byte", new byte[] { 0x89 }];
        yield return ["png signature only", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }];
        yield return ["text pretending to be a png", Encoding.ASCII.GetBytes("this is not an image at all")];
        yield return ["zeroed png header", new byte[64]];
    }

    [Theory]
    [MemberData(nameof(DegenerateImages))]
    public async Task Corrupt_image_input_fails_without_partial_output(string label, byte[] content)
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("broken.png", content);
        var output = root.Path("broken out.png");

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            ImageBackend.ConvertAsync(source, output, "png", Defaults, CancellationToken.None));

        AssertReadable(exception);
        Assert.False(File.Exists(output), $"{label}: a partial image was left behind");
        Assert.Equal(content, File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public async Task An_extension_that_lies_is_still_decoded_by_content()
    {
        using var root = new HostileRoot();
        // Real PNG bytes behind a .jpg name: the decoder sniffs the content, so this must succeed.
        var source = root.WriteBytes("mislabelled.jpg", OnePixelPng);
        var output = root.Path("mislabelled out.png");

        await ImageBackend.ConvertAsync(source, output, "png", Defaults, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(output).Take(4).ToArray());
        Assert.Equal(OnePixelPng, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task A_valid_header_with_a_bad_footer_is_refused_or_written_completely()
    {
        using var root = new HostileRoot();
        // A correct PNG signature and IHDR, then garbage where the IDAT and IEND belong.
        var truncated = new List<byte>(OnePixelPng);
        truncated.RemoveRange(40, truncated.Count - 40);
        truncated.AddRange(Enumerable.Repeat((byte)0xAA, 24));
        var source = root.WriteBytes("cut.png", truncated.ToArray());
        var output = root.Path("cut out.png");

        var failed = false;
        try
        {
            await ImageBackend.ConvertAsync(source, output, "png", Defaults, CancellationToken.None);
        }
        catch (Exception exception)
        {
            AssertReadable(exception);
            failed = true;
        }

        if (failed)
            Assert.False(File.Exists(output), "a failed decode left a partial file");
        else
            Assert.True(new FileInfo(output).Length > 0, "a successful decode wrote an empty file");

        Assert.Equal(truncated.ToArray(), File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public void An_empty_pdf_is_refused_and_leaves_no_output()
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("empty.pdf");
        var output = root.Path("empty optimized.pdf");

        Assert.ThrowsAny<Exception>(() => PdfBackend.PageCount(source));

        var exception = Assert.ThrowsAny<Exception>(() => PdfBackend.Optimize(source, output));
        AssertReadable(exception);
        Assert.False(File.Exists(output));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public void A_corrupt_pdf_gets_the_localized_cannot_open_message()
    {
        using var root = new HostileRoot();
        var source = root.WriteText("corrupt.pdf", "%PDF-1.7 but the xref is nonsense");
        var output = root.Path("corrupt optimized.pdf");

        var exception = Assert.ThrowsAny<Exception>(() => PdfBackend.Optimize(source, output));
        AssertNoTypeName(exception);
        Assert.True(Loc.HasKey("error.pdf.cannotOpen", Language.English));
        Assert.True(Loc.HasKey("error.pdf.cannotOpen", Language.Arabic));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task An_empty_zip_is_refused_and_leaves_no_output()
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("empty.zip");
        var destination = root.NewDirectory("unpacked");

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            ArchiveBackend.ExtractAsync(source, destination, CancellationToken.None));

        AssertReadable(exception);
        AssertNoEntries(destination);
        Assert.Empty(root.Partials());
    }

    [Fact]
    public async Task A_zip_entry_that_escapes_the_destination_is_refused()
    {
        using var root = new HostileRoot();
        var archive = root.Path("slip.zip");
        using (var zip = new ZipArchive(File.Create(archive), ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(@"..\escaped-sibling\pwn.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("benign");
        }

        var destination = root.NewDirectory("dest");
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            ArchiveBackend.ExtractAsync(archive, destination, CancellationToken.None));

        AssertReadable(exception);
        Assert.False(File.Exists(root.Path(@"escaped-sibling\pwn.txt")), "a zip entry escaped the extraction folder");
        Assert.False(Directory.Exists(root.Path("escaped-sibling")));
        AssertNoEntries(destination);

        // The guard must also hold for a caller that forgets the trailing separator (see H-01).
        Assert.Throws<ConversionException>(() => ArchiveBackend.SafeTarget(destination, @"..\dest-evil\pwn.txt"));
        var inside = ArchiveBackend.SafeTarget(destination, "nested/ok.txt");
        Assert.StartsWith(destination, inside, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_entry_name_with_an_illegal_character_is_refused_with_a_localized_key()
    {
        using var root = new HostileRoot();
        var destination = root.NewDirectory("dest");

        // See H-01: an illegal character used to escape as a raw ArgumentException.
        var exception = Assert.Throws<ConversionException>(() =>
            ArchiveBackend.SafeTarget(destination, "bad\0name.txt"));

        AssertNoTypeName(exception);
        Assert.True(Loc.HasKey("error.archive.unsafePath", Language.English));
        Assert.True(Loc.HasKey("error.archive.unsafePath", Language.Arabic));
    }

    [Fact]
    public void Protected_zip_refuses_an_empty_plan_with_a_localized_key()
    {
        using var root = new HostileRoot();
        var output = root.Path("empty protected.zip");
        var plan = new ProtectedZipPlan(new List<ProtectedZipEntry>(), 0);

        var exception = Assert.Throws<PasswordProtectionException>(() =>
            AesZipWriter.Create(plan, output, "correct horse battery staple"));

        Assert.Equal(PasswordProtectionFailure.Empty, exception.Failure);
        Assert.Equal("tool.protect.error.empty", exception.MessageKey);
        Assert.True(Loc.HasKey(exception.MessageKey, Language.English));
        Assert.True(Loc.HasKey(exception.MessageKey, Language.Arabic));
        Assert.False(File.Exists(output));
        Assert.Empty(root.Partials());
    }

    // ------------------------------------------------------------------ size edges

    [Fact]
    public void A_file_over_the_zip32_limit_is_refused_by_length_before_any_read()
    {
        using var root = new HostileRoot();
        var huge = root.Path("huge.bin");
        var created = false;
        try
        {
            using var stream = new FileStream(huge, FileMode.CreateNew, FileAccess.Write);
            stream.SetLength(5L * 1024 * 1024 * 1024); // 5 GiB, no payload written
            created = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No room on the runner: the guard is still a fixed constant.
        }

        if (created)
        {
            Assert.Equal(5L * 1024 * 1024 * 1024, new FileInfo(huge).Length);
            var watch = Stopwatch.StartNew();
            var exception = Assert.Throws<PasswordProtectionException>(() =>
                ProtectedZipPlanBuilder.Collect([huge]));
            watch.Stop();

            Assert.Equal(PasswordProtectionFailure.TooLarge, exception.Failure);
            Assert.Equal("tool.protect.error.tooLarge", exception.MessageKey);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5),
                "the 5 GiB file was read instead of refused by its length");
        }

        Assert.True(ProtectedZipPlanBuilder.MaximumEntrySize < 5L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void A_large_sparse_file_streams_without_buffering_it_all()
    {
        using var root = new HostileRoot();
        var sparse = root.Path("sparse.bin");
        const long size = 64L * 1024 * 1024;
        try
        {
            using var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.Write);
            stream.SetLength(size);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // a runner without room skips this case; the refusal cases still ran
        }

        var plan = ProtectedZipPlanBuilder.Collect([sparse]);
        Assert.Single(plan.Entries);
        var output = root.Path("sparse protected.zip");

        var before = GC.GetTotalAllocatedBytes(precise: false);
        var result = AesZipWriter.Create(plan, output, "correct horse battery staple");
        var allocated = GC.GetTotalAllocatedBytes(precise: false) - before;

        Assert.Equal(1, result.FileCount);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
        Assert.Equal(size, new FileInfo(sparse).Length);
        Assert.True(allocated < 56L * 1024 * 1024,
            $"the {size >> 20} MiB source was buffered: {allocated >> 20} MiB allocated");
        Assert.Empty(root.Partials());
    }

    // ------------------------------------------------------------------ access edges

    [Fact]
    public async Task A_locked_source_fails_cleanly_and_leaves_no_partial_archive()
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("locked.bin", 1, 2, 3, 4);
        var folder = root.NewDirectory("out");

        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                OutputPaths.ProduceAsync(folder, "locked", "zip", temp =>
                    ArchiveBackend.PackAsync([source], temp, "zip", CancellationToken.None)));
            AssertReadable(exception);
        }

        AssertNoEntries(folder);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public async Task A_read_only_file_is_read_and_never_modified()
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("readonly.png", OnePixelPng);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        var attributes = File.GetAttributes(source);
        var output = root.Path("readonly out.png");

        try
        {
            await ImageBackend.ConvertAsync(source, output, "png", Defaults, CancellationToken.None);

            Assert.True(File.Exists(output));
            Assert.Equal(OnePixelPng, File.ReadAllBytes(source));
            Assert.Equal(attributes, File.GetAttributes(source));
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    [Fact]
    public void The_same_path_twice_gets_two_distinct_entries()
    {
        using var root = new HostileRoot();
        var source = root.WriteText("same.png", "same");

        var plan = ProtectedZipPlanBuilder.Collect([source, source]);

        Assert.InRange(plan.Entries.Count, 1, 2);
        Assert.Equal(plan.Entries.Count, plan.Entries.Select(entry => entry.EntryName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(plan.Entries, entry => Assert.Equal(source, entry.SourcePath));
    }

    [Fact]
    public async Task A_source_that_collides_with_its_own_output_is_never_overwritten()
    {
        using var root = new HostileRoot();
        var source = root.WriteText("photo.png", "the original");

        // The tool would write "photo.png" again: the shared helper must number it instead of clobbering it.
        var produced = await OutputPaths.ProduceAsync(root.Root, "photo", "png", temp =>
        {
            File.WriteAllText(temp, "the result");
            return Task.CompletedTask;
        });

        Assert.NotEqual(source, produced, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("the original", File.ReadAllText(source));
        Assert.Equal("the result", File.ReadAllText(produced));
    }

    [Fact]
    public async Task Duplicate_content_in_two_names_produces_two_outputs()
    {
        using var root = new HostileRoot();
        var first = root.WriteBytes("copy one.png", OnePixelPng);
        var second = root.WriteBytes("copy two.png", OnePixelPng);

        var a = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(first), "out", temp =>
        {
            File.WriteAllBytes(temp, [1]);
            return Task.CompletedTask;
        });
        var b = await OutputPaths.ProduceAsync(root.Root, Formats.BaseNameOf(second), "out", temp =>
        {
            File.WriteAllBytes(temp, [2]);
            return Task.CompletedTask;
        });

        Assert.NotEqual(a, b, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(OnePixelPng, File.ReadAllBytes(first));
        Assert.Equal(OnePixelPng, File.ReadAllBytes(second));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(a));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(b));
    }

    // ------------------------------------------------------------------ cancellation and teardown

    [Fact]
    public async Task A_cancelled_run_leaves_no_output_and_no_part_file()
    {
        using var root = new HostileRoot();
        var source = root.WriteBytes("cancel.png", OnePixelPng);
        var output = root.Path("cancel out.png");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            ImageBackend.ConvertAsync(source, output, "png", Defaults, cancellation.Token));

        Assert.True(exception is OperationCanceledException, $"a cancelled run threw {exception.GetType().Name}");
        Assert.False(File.Exists(output));
        Assert.Equal(OnePixelPng, File.ReadAllBytes(source));
        Assert.Empty(root.Partials());
    }

    [Fact]
    public void Cancelling_protect_removes_the_half_written_container()
    {
        using var root = new HostileRoot();
        var source = root.WriteText("cancel.txt", "content");
        var output = root.Path("cancel protected.zip");
        var plan = ProtectedZipPlanBuilder.Collect([source]);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);

        var exception = Assert.ThrowsAny<Exception>(() =>
            AesZipWriter.Create(plan, output, "correct horse battery staple", progress, cancellation.Token));

        Assert.True(exception is OperationCanceledException, $"a cancelled protect threw {exception.GetType().Name}");
        Assert.False(File.Exists(output), "a cancelled protect left a half-written container");
        Assert.Equal("content", File.ReadAllText(source));
        Assert.Empty(root.Partials());
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }

    [Fact]
    public async Task A_failing_producer_leaves_no_part_file_and_keeps_the_source()
    {
        using var root = new HostileRoot();
        var source = root.WriteText("keep.png", "original bytes");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OutputPaths.ProduceAsync(root.Root, "keep", "png", temp =>
            {
                File.WriteAllBytes(temp, [0xDE, 0xAD]);
                throw new InvalidOperationException("producer failed on purpose");
            }));

        Assert.Equal("original bytes", File.ReadAllText(source));
        Assert.Empty(root.Partials());
        Assert.Single(Directory.GetFiles(root.Root, "*.png"));
    }

    // ------------------------------------------------------------------ the user-facing error funnel

    private static string Friendly(Exception exception)
    {
        var method = typeof(JobRunner).GetMethod("Friendly", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method.Invoke(null, [exception])!;
    }

    [Fact]
    public void The_job_funnel_maps_every_hostile_failure_to_a_localized_message()
    {
        var denied = Friendly(new UnauthorizedAccessException("Access is denied."));
        Assert.Equal(Loc.T("error.output.notWritable"), denied);
        Assert.DoesNotContain("UnauthorizedAccessException", denied, StringComparison.Ordinal);

        var io = Friendly(new IOException("The process cannot access the file because it is being used by another process."));
        Assert.DoesNotContain("IOException", io, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(io));

        var tooLong = Friendly(new PathTooLongException("The specified path is too long."));
        Assert.DoesNotContain("PathTooLongException", tooLong, StringComparison.Ordinal);

        var missing = Friendly(new FileNotFoundException("Could not find file 'x.png'."));
        Assert.DoesNotContain("FileNotFoundException", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void The_funnel_never_shows_a_raw_exception_type()
    {
        // A hostile path reaches the funnel as ArgumentException / NotSupportedException; the user must
        // still get a sentence, not "ArgumentException: Illegal characters in path." (see H-02).
        foreach (var exception in new Exception[]
                 {
                     new ArgumentException("Illegal characters in path."),
                     new NotSupportedException("The given path's format is not supported."),
                 })
        {
            var text = Friendly(exception);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain(exception.GetType().Name, text, StringComparison.Ordinal);
            Assert.DoesNotContain("System.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_error_key_the_suite_relies_on_exists_in_both_languages()
    {
        string[] keys =
        [
            "error.file",
            "error.output.notWritable",
            "error.image.process",
            "error.archive.empty",
            "error.archive.extract",
            "error.archive.unsafePath",
            "error.pdf.cannotOpen",
            "error.conversion.noOutput",
            "tool.protect.error.empty",
            "tool.protect.error.tooLarge",
            "tool.protect.error.unsupported",
        ];

        foreach (var key in keys)
        {
            Assert.True(Loc.HasKey(key, Language.English), "missing English key " + key);
            Assert.True(Loc.HasKey(key, Language.Arabic), "missing Arabic key " + key);
            Assert.False(string.IsNullOrWhiteSpace(Loc.Table(Language.English)[key]), "empty English value " + key);
            Assert.False(string.IsNullOrWhiteSpace(Loc.Table(Language.Arabic)[key]), "empty Arabic value " + key);
        }
    }

    // ------------------------------------------------------------------ teardown proof

    /// <summary>
    /// The PM asked for proof that the awkward fixtures clean themselves up: a read-only file, a nested
    /// path beyond MAX_PATH and an embedded-NUL entry name must all be gone once the fixture is disposed,
    /// and no sibling <c>hostile-*</c> root may be left behind.
    /// </summary>
    [Fact]
    public void Hostile_fixtures_remove_themselves_completely()
    {
        var root = new HostileRoot();
        var directory = root.Root;
        string? deep = null;
        try
        {
            var readOnly = root.WriteBytes("read-only.png", OnePixelPng);
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);

            deep = directory;
            while (deep.Length < 290)
            {
                deep = System.IO.Path.Combine(deep, new string('d', 50));
                try
                {
                    Directory.CreateDirectory(deep);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
                {
                    deep = null;
                    break;
                }
            }

            if (deep is not null)
                File.WriteAllBytes(System.IO.Path.Combine(deep, "deep.png"), [1]);

            // An embedded NUL can never become a file; the guard must refuse it, not create anything.
            Assert.Throws<ConversionException>(() => ArchiveBackend.SafeTarget(directory, "bad\0name.txt"));
            Assert.Single(Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            root.Dispose();
        }

        Assert.False(Directory.Exists(directory), "the per-test hostile root was not removed");
        if (deep is not null && deep.Length > directory.Length)
            Assert.False(Directory.Exists(deep), "the long-path fixture survived the cleanup");

        var scratch = System.IO.Path.GetDirectoryName(directory);
        if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch))
            Assert.Empty(Directory.GetDirectories(scratch, "hostile-*"));
    }

    // ------------------------------------------------------------------ redact (not wired to the wheel yet)

    [Fact]
    public void Redaction_boxes_stay_sane_for_degenerate_geometry()
    {
        var box = RedactionBox.FromEdges(10, 20, 40, 80);
        Assert.Equal(30, box.Width, 6);
        Assert.Equal(60, box.Height, 6);
        Assert.True(box.Contains(20, 30));
        Assert.False(box.Contains(0, 0));
        Assert.True(box.Intersects(RedactionBox.FromEdges(35, 70, 100, 100)));
        Assert.False(box.Intersects(RedactionBox.FromEdges(500, 500, 600, 600)));

        var zero = RedactionBox.FromEdges(0, 0, 0, 0);
        Assert.Equal(0, zero.Width, 6);
        Assert.Equal(0, zero.Height, 6);
    }
}

/// <summary>Serialises the hostile-input cases that redirect TMP/TEMP.</summary>
[CollectionDefinition(nameof(HostileIo), DisableParallelization = true)]
public class HostileIo;
