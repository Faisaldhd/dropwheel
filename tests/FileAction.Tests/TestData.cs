using System.IO;

namespace FileAction.Tests;

/// <summary>
/// Real-world sample files live outside the repository in <c>_local\testdata\TestFiles</c> (owner's rule:
/// <c>_local\</c> is never committed). The tests prefer them when the folder is present — they exercise the
/// awkward real formats (HEIC, RAR, 7z, FLAC, a 50-page PDF, a read-only file) instead of the tiny files the
/// fixture generates. CI has no <c>_local\</c>, so it keeps using the generated samples.
/// </summary>
internal static class TestData
{
    /// <summary>The fixture folder, or null when the tests are running without one (CI).</summary>
    public static string? FixtureRoot { get; } = Resolve();

    private static string? Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("DROPWHEEL_TESTDATA");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        var repo = RepoRoot();
        if (repo is null)
            return null;
        var candidate = Path.GetFullPath(Path.Combine(repo, "..", "_local", "testdata", "TestFiles"));
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// Copies the named fixtures into <paramref name="destination"/> and points <paramref name="files"/> at the
    /// copies, so a test can never modify the owner's originals. The read-only fixture keeps its attribute,
    /// because that is exactly what the read-only test needs.
    /// </summary>
    public static void ApplyFixtures(string destination, IDictionary<string, string> files)
    {
        if (FixtureRoot is null)
            return;

        // sample key -> (fixture file, keep read-only)
        var map = new (string Key, string File, bool ReadOnly)[]
        {
            ("png", "photo.png", false),
            ("jpg", "photo.jpg", false),
            ("pdf", "doc.pdf", false),
            ("md", "notes.md", false),
            ("csv", "data.csv", false),
            ("srt", "subs.srt", false),
            ("xlsx", "sheet.xlsx", false),
            ("docx", "report.docx", false),
            ("pptx", "deck.pptx", false),
            ("mp4", "clip.mp4", false),
            ("mp3", "song.mp3", false),
            ("gif", "anim.gif", false),
            ("zip", "bundle.zip", false),
            ("7z", "bundle.7z", false),
            ("rar", "bundle.rar", false),
            ("readonly", "read-only.jpg", true),
        };

        foreach (var (key, name, readOnly) in map)
        {
            var source = Path.Combine(FixtureRoot, name);
            if (!File.Exists(source))
                continue;
            var copy = Path.Combine(destination, "fixture-" + name);
            File.Copy(source, copy, overwrite: true);
            if (readOnly)
                File.SetAttributes(copy, File.GetAttributes(copy) | FileAttributes.ReadOnly);
            files[key] = copy;
        }

        var sourceFolder = Path.Combine(FixtureRoot, "folder");
        if (Directory.Exists(sourceFolder))
        {
            var copyFolder = Path.Combine(destination, "fixture-folder");
            foreach (var file in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceFolder, file);
                var target = Path.Combine(copyFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            files["folder"] = copyFolder;
        }
    }
}
