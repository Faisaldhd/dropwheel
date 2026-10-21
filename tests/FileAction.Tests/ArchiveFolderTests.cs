using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FileAction.App;
using FileAction.App.Actions;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Backends;
using SharpCompress.Archives;

namespace FileAction.Tests;

/// <summary>
/// 2.4.7 archive/folder acceptance: reparse points are never followed, empty directories survive
/// pack/extract/unlock, same-named entries are disambiguated, folders do not open an empty Convert wheel,
/// and a folder cannot be copied into its own subtree.
/// </summary>
public sealed class ArchiveFolderTests
{
    private const string Password = "correct horse battery staple";

    [Theory]
    [InlineData("bundle.zip")]
    [InlineData("bundle.7z")]
    [InlineData("bundle.rar")]
    public async Task Real_world_archives_extract_their_tree(string name)
    {
        var fixture = TestData.FixtureRoot;
        if (fixture is null)
            return;
        var source = Path.Combine(fixture, name);
        if (!File.Exists(source))
            return;

        using var root = new TempTree();
        var extracted = await ArchiveBackend.ExtractAsync(source, root.Directory, default);

        Assert.True(File.Exists(Path.Combine(extracted, "hello.txt")), $"{name} did not extract hello.txt");
        Assert.True(
            File.Exists(Path.Combine(extracted, "nested", "data.txt")) ||
            File.Exists(Path.Combine(extracted, "sub", "data.txt")),
            $"{name} did not extract its nested file");
    }

    [Fact]
    public async Task The_folder_fixture_packs_and_extracts_without_losing_its_tree()
    {
        var fixture = TestData.FixtureRoot;
        if (fixture is null)
            return;
        var folder = Path.Combine(fixture, "folder");
        if (!Directory.Exists(folder))
            return;

        using var root = new TempTree();
        foreach (var format in new[] { "zip", "tar.gz" })
        {
            var archive = Path.Combine(root.Directory, "folder." + format);
            await ArchiveBackend.PackAsync([folder], archive, format, default);

            var destination = Path.Combine(root.Directory, "out-" + format.Replace('.', '-'));
            var extracted = await ArchiveBackend.ExtractAsync(archive, destination, default);

            Assert.True(File.Exists(Path.Combine(extracted, "readme.txt")), $"{format} lost readme.txt");
            Assert.True(File.Exists(Path.Combine(extracted, "sub", "nested.txt")), $"{format} lost sub/nested.txt");
        }
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("tar.gz")]
    public async Task Empty_folders_survive_pack_and_extract(string format)
    {
        using var root = new TempTree();
        var tree = Path.Combine(root.Directory, "tree");
        System.IO.Directory.CreateDirectory(Path.Combine(tree, "empty"));
        System.IO.Directory.CreateDirectory(Path.Combine(tree, "nested", "empty2"));
        File.WriteAllText(Path.Combine(tree, "file.txt"), "kept");

        var archive = Path.Combine(root.Directory, "tree." + format);
        await ArchiveBackend.PackAsync([tree], archive, format, default);
        if (format == "zip")
        {
            using var zip = ZipFile.OpenRead(archive);
            var names = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("tree/empty/", names);
            Assert.Contains("tree/nested/empty2/", names);
        }

        var destination = Path.Combine(root.Directory, "out");
        var extracted = await ArchiveBackend.ExtractAsync(archive, destination, default);

        Assert.True(System.IO.Directory.Exists(Path.Combine(extracted, "empty")), $"{format} lost tree/empty");
        Assert.True(System.IO.Directory.Exists(Path.Combine(extracted, "nested", "empty2")), $"{format} lost tree/nested/empty2");
        Assert.Equal("kept", File.ReadAllText(Path.Combine(extracted, "file.txt")));
    }

    [Fact]
    public async Task Same_named_files_from_different_folders_are_disambiguated()
    {
        using var root = new TempTree();
        var a = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "a")).FullName;
        var b = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "b")).FullName;
        File.WriteAllText(Path.Combine(a, "report.txt"), "from a");
        File.WriteAllText(Path.Combine(b, "report.txt"), "from b");

        var archive = Path.Combine(root.Directory, "collision.zip");
        await ArchiveBackend.PackAsync(
            [Path.Combine(a, "report.txt"), Path.Combine(b, "report.txt")], archive, "zip", default);

        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(2, zip.Entries.Count);
        Assert.Equal("from a", ReadEntry(zip, "report.txt"));
        Assert.Equal("from b", ReadEntry(zip, "report (2).txt"));
    }

    [Fact]
    public async Task Same_named_folder_roots_are_disambiguated()
    {
        using var root = new TempTree();
        var first = Path.Combine(root.Directory, "one", "docs");
        var second = Path.Combine(root.Directory, "two", "docs");
        System.IO.Directory.CreateDirectory(first);
        System.IO.Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "a.txt"), "a");
        File.WriteAllText(Path.Combine(second, "b.txt"), "b");

        var archive = Path.Combine(root.Directory, "folders.zip");
        await ArchiveBackend.PackAsync([first, second], archive, "zip", default);

        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal("a", ReadEntry(zip, "docs/a.txt"));
        Assert.Equal("b", ReadEntry(zip, "docs (2)/b.txt"));
    }

    [Fact]
    public async Task Packing_skips_junctions_and_never_reaches_outside_the_tree()
    {
        using var root = new TempTree();
        var outside = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "must not be packed");
        var tree = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "tree")).FullName;
        File.WriteAllText(Path.Combine(tree, "visible.txt"), "visible");

        Assert.True(
            TryCreateJunction(Path.Combine(tree, "linked"), outside),
            "could not create a junction for the reparse-point test");

        var archive = Path.Combine(root.Directory, "junction.zip");
        await ArchiveBackend.PackAsync([tree], archive, "zip", default);

        using var zip = ZipFile.OpenRead(archive);
        var names = zip.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("tree/visible.txt", names);
        Assert.DoesNotContain(names, name => name.Contains("linked", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unlocking_keeps_empty_folders()
    {
        using var root = new TempTree();
        var folder = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "locked")).FullName;
        System.IO.Directory.CreateDirectory(Path.Combine(folder, "empty"));
        File.WriteAllText(Path.Combine(folder, "note.txt"), "hello");

        var protectedArchive = Path.Combine(root.Directory, "locked Protected.zip");
        AesZipWriter.Create(ProtectedZipPlanBuilder.Collect([folder]), protectedArchive, Password);

        var unlocked = Path.Combine(root.Directory, "locked Unlocked.zip");
        var result = ZipPasswordUnlocker.Unlock(protectedArchive, unlocked, Password);
        Assert.Equal(1, result.FileCount);

        using (var archive = ArchiveFactory.OpenArchive(unlocked))
        {
            var entries = archive.Entries.ToList();
            Assert.Contains(entries, entry => entry.IsDirectory && entry.Key == "locked/empty/");
            Assert.Contains(entries, entry => !entry.IsDirectory && entry.Key == "locked/note.txt");
        }

        var destination = Path.Combine(root.Directory, "restored");
        var extracted = await ArchiveBackend.ExtractAsync(unlocked, destination, default);
        Assert.True(System.IO.Directory.Exists(Path.Combine(extracted, "empty")), "unlock lost the empty folder");
    }

    [Fact]
    public void A_folder_opens_the_tools_wheel_with_its_archive_category()
    {
        using var root = new TempTree();
        var folder = System.IO.Directory.CreateDirectory(Path.Combine(root.Directory, "folder")).FullName;
        var items = new[] { new FileItem(folder) };
        var tools = new ToolLocator(root.Directory);
        var operations = WheelController.BuildOperations();

        Assert.Empty(WheelController.Offered(WheelKind.Convert, items, tools, new AppSettings(), operations));
        Assert.Equal(WheelKind.Tools, WheelController.InitialWheel(items, operations, tools, new AppSettings()));

        var offered = WheelController.Offered(WheelKind.Tools, items, tools, new AppSettings(), operations);
        var archive = Assert.Single(offered.OfType<WheelGroupOperation>(), group => group.GroupId == "category.archive");
        Assert.Equal("Archive", archive.Label);
    }

    [Fact]
    public void An_unknown_extension_opens_the_tools_wheel_instead_of_an_empty_convert()
    {
        using var root = new TempTree();
        var file = Path.Combine(root.Directory, "mystery.xyz");
        File.WriteAllText(file, "xyz");
        var items = new[] { new FileItem(file) };
        var tools = new ToolLocator(root.Directory);
        var operations = WheelController.BuildOperations();

        Assert.Empty(WheelController.Offered(WheelKind.Convert, items, tools, new AppSettings(), operations));
        Assert.Equal(WheelKind.Tools, WheelController.InitialWheel(items, operations, tools, new AppSettings()));
        Assert.NotEmpty(WheelController.Offered(WheelKind.Tools, items, tools, new AppSettings(), operations));
    }

    [Fact]
    public void A_known_file_still_opens_convert()
    {
        using var root = new TempTree();
        var file = Path.Combine(root.Directory, "photo.png");
        File.WriteAllBytes(file, [1, 2, 3]);
        var items = new[] { new FileItem(file) };
        var tools = new ToolLocator(root.Directory);
        var operations = WheelController.BuildOperations();

        Assert.Equal(WheelKind.Convert, WheelController.InitialWheel(items, operations, tools, new AppSettings()));
    }

    [Theory]
    [InlineData(@"C:\root\folder", @"C:\root\folder", true)]
    [InlineData(@"C:\root\folder", @"C:\root\folder\sub", true)]
    [InlineData(@"C:\root\folder", @"C:\root\folder\sub\deeper", true)]
    [InlineData(@"C:\root\folder\", @"C:\root\folder\sub", true)]
    [InlineData(@"C:\root\folder", @"C:\root\folder2", false)]
    [InlineData(@"C:\root\folder", @"C:\root", false)]
    [InlineData(@"C:\", @"C:\folder", true)]
    [InlineData(@"C:\folder", @"C:\", false)]
    public void Copying_a_folder_into_itself_or_below_is_refused(string folder, string target, bool expected) =>
        Assert.Equal(expected, FileActions.IsSameOrInside(folder, target));

    // ------------------------------------------------------------------ helpers

    private static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Creates an NTFS junction; false when this machine/user cannot create one.</summary>
    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(start);
            if (process is null)
                return false;
            process.WaitForExit(5000);
            return process.HasExited && process.ExitCode == 0 &&
                   System.IO.Directory.Exists(link) &&
                   (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private sealed class TempTree : IDisposable
    {
        public TempTree()
        {
            Directory = Path.Combine(ScratchRoot(), "archive-folder-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        /// <summary>
        /// The repo's git-ignored scratch area rather than %TEMP%: this PC denies writes below the profile's
        /// Temp folder, and the other suites already use <c>_local\tmp</c> / <c>_audit-ci</c> the same way.
        /// </summary>
        private static string ScratchRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
                directory = directory.Parent;
            if (directory is not null)
            {
                return System.IO.Directory.Exists(Path.Combine(directory.FullName, "_local"))
                    ? Path.Combine(directory.FullName, "_local", "tmp")
                    : Path.Combine(directory.FullName, "_audit-ci");
            }

            // The reflection runner lives in _local\tmp and has no solution above it.
            var local = AppContext.BaseDirectory.IndexOf(@"\_local\", StringComparison.OrdinalIgnoreCase);
            return local >= 0
                ? Path.Combine(AppContext.BaseDirectory[..local], "_local", "tmp")
                : Path.Combine(Path.GetTempPath(), "dropwheel-archive-tests");
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
