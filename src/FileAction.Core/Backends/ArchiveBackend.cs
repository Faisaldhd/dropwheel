using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using static FileAction.Core.Loc;

namespace FileAction.Core.Backends;

/// <summary>Extracting (ZIP, TAR, GZ, BZ2, XZ, RAR, 7z) and packing (ZIP, TAR, TAR.GZ).</summary>
public static class ArchiveBackend
{
    private static readonly ExtractionOptions Options = new() { ExtractFullPath = true, Overwrite = true, PreserveFileTime = true };

    /// <summary>
    /// Extracts next to the archive. When the archive holds exactly one top-level item it is placed
    /// directly in <paramref name="destinationDir"/>; otherwise everything goes into a folder named after the archive.
    /// </summary>
    public static async Task<string> ExtractAsync(string archive, string destinationDir, CancellationToken ct)
    {
        var temp = Path.Combine(destinationDir, $".{Formats.BaseNameOf(archive)}.{Guid.NewGuid().ToString("N")[..8]}.part");
        Directory.CreateDirectory(temp);
        try
        {
            await Task.Run(() => ExtractTo(archive, temp, ct), ct).ConfigureAwait(false);

            // A .tar.gz read as plain gzip yields a single .tar – unpack that too.
            var only = Directory.GetFileSystemEntries(temp);
            if (only.Length == 1 && File.Exists(only[0]) && Formats.KeyOf(only[0]) == "tar")
            {
                var inner = only[0];
                var innerDir = Path.Combine(temp, ".inner");
                Directory.CreateDirectory(innerDir);
                ExtractTo(inner, innerDir, ct);
                File.Delete(inner);
                foreach (var e in Directory.GetFileSystemEntries(innerDir))
                    MoveEntry(e, Path.Combine(temp, Path.GetFileName(e)));
                Directory.Delete(innerDir);
            }

            var entries = Directory.GetFileSystemEntries(temp);
            if (entries.Length == 0)
                throw new ConversionException(T("error.archive.empty"));
            if (entries.Length == 1)
            {
                var name = Path.GetFileName(entries[0]);
                var isDir = Directory.Exists(entries[0]);
                var target = isDir
                    ? OutputPaths.UniqueDirectory(destinationDir, name)
                    : OutputPaths.Unique(destinationDir, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name).TrimStart('.'));
                MoveEntry(entries[0], target);
                return target;
            }
            var folder = OutputPaths.UniqueDirectory(destinationDir, Formats.BaseNameOf(archive));
            Directory.Move(temp, folder);
            return folder;
        }
        finally
        {
            OutputPaths.TryDelete(temp);
        }
    }

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from))
            Directory.Move(from, to);
        else
            File.Move(from, to);
    }

    private static void ExtractTo(string archive, string destination, CancellationToken ct)
    {
        var fullDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        // RAR stores a directory as a zero-byte entry without a trailing slash, so the reader reports it
        // as a file and the child entry then fails because that path is already a file. Keys that prefix
        // another key are directories regardless of how the format marks the entry.
        HashSet<string> directoryHints = [];
        try
        {
            if (Formats.KeyOf(archive) == "rar")
                directoryHints = NestedDirectoryKeys(archive);
            if (Formats.KeyOf(archive) == "7z")
            {
                using var a = ArchiveFactory.OpenArchive(archive, new ReaderOptions());
                foreach (var entry in a.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.Key is null)
                        continue;
                    var path = SafeTarget(fullDestination, entry.Key);
                    if (entry.IsDirectory)
                    {
                        // A directory entry is all an empty folder has; without this the folder is lost.
                        Directory.CreateDirectory(path);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    entry.WriteToFile(path, Options);
                }
                return;
            }

            using var stream = File.OpenRead(archive);
            using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                var entry = reader.Entry;
                var key = entry.Key;
                if (key is not null && (entry.IsDirectory ||
                                        directoryHints.Contains(key.Replace('\\', '/').TrimEnd('/'))))
                {
                    if (!string.IsNullOrEmpty(key))
                        Directory.CreateDirectory(SafeTarget(fullDestination, key));
                    continue;
                }
                if (string.IsNullOrEmpty(key))
                    key = Formats.BaseNameOf(archive); // single-file .gz/.bz2/.xz
                var path = SafeTarget(fullDestination, key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                reader.WriteEntryToFile(path, Options);
            }
        }
        catch (Exception ex) when (ex is InvalidFormatException or ArchiveOperationException or CryptographicException)
        {
            throw new ConversionException(T("error.archive.extract", ex.Message), ex);
        }
    }

    /// <summary>Keys that another key nests below; RAR's directory records have no trailing slash.</summary>
    private static HashSet<string> NestedDirectoryKeys(string archive)
    {
        var keys = new List<string>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var stream = File.OpenRead(archive))
        using (var reader = ReaderFactory.OpenReader(stream, new ReaderOptions()))
        {
            while (reader.MoveToNextEntry())
            {
                var key = reader.Entry.Key;
                if (string.IsNullOrEmpty(key))
                    continue;
                var normalized = key.Replace('\\', '/');
                var trimmed = normalized.TrimEnd('/');
                keys.Add(trimmed);
                if (normalized.EndsWith('/'))
                    directories.Add(trimmed);
            }
        }

        foreach (var key in keys)
        {
            for (var slash = key.IndexOf('/'); slash > 0; slash = key.IndexOf('/', slash + 1))
                directories.Add(key[..slash]);
        }
        return directories;
    }

    /// <summary>Rejects entries like "../../evil.exe" that would escape the target folder.</summary>
    public static string SafeTarget(string fullDestination, string entryKey)
    {
        // The separator is added here so the guard does not depend on the caller remembering it: without
        // it, "..\dest-evil\pwn.txt" starts with "…\dest" and slips out of the destination.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullDestination)) + Path.DirectorySeparatorChar;
        var relative = entryKey.Replace('\\', '/').TrimStart('/');
        string path;
        try
        {
            path = Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A name the file system cannot represent (an embedded NUL, a device path) is unsafe too.
            throw new ConversionException(T("error.archive.unsafePath", entryKey), ex);
        }

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ConversionException(T("error.archive.unsafePath", entryKey));
        return path;
    }

    /// <summary>Packs files and folders into one archive ("zip", "tar" or "tar.gz").</summary>
    public static async Task PackAsync(IReadOnlyList<string> items, string output, string format, CancellationToken ct)
    {
        var entries = new List<PackEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var trimmed = item.TrimEnd('\\', '/');
            if (Directory.Exists(trimmed))
            {
                // A selected junction or symlink is a link, not content: never follow it and never walk
                // outside the tree the user selected (the same rule the protect plan already applies).
                if (IsReparsePoint(trimmed))
                    continue;
                var root = Path.GetFileName(trimmed);
                if (string.IsNullOrEmpty(root))
                    root = "root";
                var prefix = Unique(root + "/", names, directory: true);
                var before = entries.Count;
                CollectDirectory(trimmed, prefix, entries, names, ct);
                if (entries.Count == before)
                    entries.Add(new PackEntry(trimmed, prefix, IsDirectory: true));
            }
            else if (File.Exists(trimmed))
            {
                if (!IsReparsePoint(trimmed))
                    entries.Add(new PackEntry(trimmed, Unique(Path.GetFileName(trimmed), names, directory: false), IsDirectory: false));
            }
            else
            {
                // Keep the old failure path: CreateEntryFromFile reports the missing file.
                entries.Add(new PackEntry(trimmed, Unique(Path.GetFileName(trimmed), names, directory: false), IsDirectory: false));
            }
        }

        await Task.Run(async () =>
        {
            await using var file = File.Create(output);
            if (format == "zip")
            {
                using var zip = new ZipArchive(file, ZipArchiveMode.Create);
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.IsDirectory)
                        zip.CreateEntry(entry.Name);
                    else
                        zip.CreateEntryFromFile(entry.Source, entry.Name, CompressionLevel.Optimal);
                }
                return;
            }

            Stream target = format == "tar.gz" ? new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true) : file;
            try
            {
                await using var tar = new TarWriter(target, TarEntryFormat.Pax, leaveOpen: true);
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    await tar.WriteEntryAsync(entry.Source, entry.Name.TrimEnd('/'), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                if (target != file)
                    await target.DisposeAsync().ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>One ZIP/TAR entry; directory entries keep their trailing slash.</summary>
    private readonly record struct PackEntry(string Source, string Name, bool IsDirectory);

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable root is not followed either; the failure will resurface on the entry.
            return false;
        }
    }

    /// <summary>
    /// Adds one directory tree. Junctions and symlinks are skipped instead of followed, empty folders are
    /// kept as explicit directory entries, and repeated names get the protect plan's " (2)" suffix.
    /// </summary>
    private static void CollectDirectory(
        string directory,
        string prefix,
        List<PackEntry> entries,
        HashSet<string> names,
        CancellationToken ct)
    {
        var children = new DirectoryInfo(directory).EnumerateFileSystemInfos()
            .OrderBy(child => child.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = child.Attributes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // disappeared while packing; the tree may not be changed under us
            }

            // Reparse points (junctions, directory symlinks) could point outside the selected tree. A file
            // symlink is skipped too: the ZIP should not silently include content from another place.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            if ((attributes & FileAttributes.Directory) != 0)
            {
                var childPrefix = Unique(prefix + child.Name + "/", names, directory: true);
                var before = entries.Count;
                CollectDirectory(child.FullName, childPrefix, entries, names, ct);
                if (entries.Count == before)
                    entries.Add(new PackEntry(child.FullName, childPrefix, IsDirectory: true));
            }
            else
            {
                entries.Add(new PackEntry(child.FullName, Unique(prefix + child.Name, names, directory: false), IsDirectory: false));
            }
        }
    }

    /// <summary>Matches <see cref="ProtectedZipPlanBuilder"/>: "name (2).ext", or "name (2)/" for folders.</summary>
    private static string Unique(string name, HashSet<string> names, bool directory)
    {
        if (names.Add(name))
            return name;

        var suffix = directory ? "/" : string.Empty;
        var bare = directory ? name[..^1] : name;
        var extension = Path.GetExtension(bare);
        var stem = extension.Length > 0 ? bare[..^extension.Length] : bare;
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){extension}{suffix}";
            if (names.Add(candidate))
                return candidate;
        }
    }

    public static string ArchiveNameFor(IReadOnlyList<FileItem> items) =>
        items.Count == 1 ? items[0].BaseName : T("fileType.archive");

    /// <summary>Repacks an archive into another archive format.</summary>
    public static async Task<string> RepackAsync(FileItem item, string format, OperationContext ctx)
    {
        var temp = OutputPaths.TempDirectory();
        try
        {
            var extracted = await ExtractAsync(item.Path, temp, ctx.CancellationToken).ConfigureAwait(false);
            var contents = Directory.Exists(extracted) && Path.GetFileName(extracted) == item.BaseName
                ? Directory.GetFileSystemEntries(extracted)
                : [extracted];
            return await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, tmp =>
                PackAsync(contents, tmp, format, ctx.CancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(temp);
        }
    }
}
