using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>A file inside a folder could not be read because another process holds it open.</summary>
public sealed class LockFileInUseException(string relativePath)
    : IOException($"'{relativePath}' is open in another program; close it and try again.")
{
    public string RelativePath { get; } = relativePath;
}

/// <summary>
/// The equivalent of a tar stream, written directly into authenticated frames: relative UTF-8 paths with
/// forward slashes, timestamps, attributes and sizes, then the file bytes. Links and junctions are skipped
/// (they could lead outside the folder), and the folder's own <c>desktop.ini</c> is handled separately so
/// it can be restored byte for byte.
/// </summary>
public static class LockArchive
{
    public const byte EntryEnd = 0;
    public const byte EntryFile = 1;
    public const byte EntryDirectory = 2;

    private const int CopyBuffer = 256 * 1024;

    /// <summary>Writes the archive for <paramref name="root"/> into the container's content sink.</summary>
    public static int Write(
        string root,
        ILockContentSink sink,
        IProgress<string>? currentFile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(sink);
        var count = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        var buffer = new byte[CopyBuffer];
        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                string[] entries;
                try
                {
                    entries = Directory.GetFileSystemEntries(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new LockFileInUseException(LockPaths.ToArchivePath(root, directory));
                }
                Array.Sort(entries, StringComparer.OrdinalIgnoreCase);

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue; // symlink or junction: never followed

                    var relative = LockPaths.ToArchivePath(root, entry);
                    if (relative.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                        continue; // saved separately in the metadata block

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        WriteHeader(sink, EntryDirectory, relative, File.GetLastWriteTimeUtc(entry).Ticks,
                            (int)attributes & 0xFFFF, 0);
                        count++;
                        pending.Push(entry);
                        continue;
                    }

                    var info = new FileInfo(entry);
                    var length = info.Length;
                    currentFile?.Report(LockPaths.DisplayName(entry));
                    WriteHeader(sink, EntryFile, relative, info.LastWriteTimeUtc.Ticks, (int)attributes & 0xFFFF, length);
                    count++;
                    CopyFile(entry, relative, length, sink, buffer);
                }
            }

            sink.Write([EntryEnd]);
            return count;
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    /// <summary>Counts the entries <see cref="Write"/> would store, without reading any file bytes.</summary>
    public static int CountEntries(string root)
    {
        var count = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                var relative = LockPaths.ToArchivePath(root, entry);
                if (relative.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                    continue;
                count++;
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
            }
        }
        return count;
    }

    /// <summary>
    /// Restores the archive under <paramref name="destinationRoot"/>, which must not already contain the
    /// entries. Every path is validated before use, so an archive cannot write outside the destination.
    /// </summary>
    public static int Extract(Stream archive, string destinationRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(destinationRoot);
        Directory.CreateDirectory(destinationRoot);
        var restore = new List<(string Path, DateTime Modified, DateTime Created, int Attributes, bool Directory)>();
        var count = 0;
        var buffer = new byte[CopyBuffer];
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var kind = ReadByte(archive);
                if (kind == EntryEnd)
                    break;
                if (kind is not (EntryFile or EntryDirectory))
                    throw new LockDamagedException("archive entry kind");

                var pathLength = ReadUInt16(archive);
                if (pathLength is 0 or > 1024)
                    throw new LockDamagedException("archive path length");
                var relative = ReadUtf8(archive, pathLength);
                var modifiedTicks = ReadInt64(archive);
                var createdTicks = modifiedTicks;
                var attributes = ReadInt32(archive);
                var length = kind == EntryFile ? ReadInt64(archive) : 0;
                if (length < 0)
                    throw new LockDamagedException("archive entry length");

                var target = LockPaths.CombineSafe(destinationRoot, relative);
                if (kind == EntryDirectory)
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        CopyBuffer, FileOptions.SequentialScan);
                    CopyExactly(archive, file, length, buffer);
                    file.Flush(flushToDisk: false);
                }
                restore.Add((target, FromUtcTicks(modifiedTicks), FromUtcTicks(createdTicks),
                    attributes & 0xFFFF, kind == EntryDirectory));
                count++;
            }

            if (archive.ReadByte() != -1)
                throw new LockDamagedException("archive trailing data");

            // Read-only attributes are applied last, and directories after their children, so nothing
            // prevents the writes or timestamp updates above.
            foreach (var entry in restore.OrderByDescending(e => e.Directory))
            {
                try
                {
                    if (entry.Directory)
                    {
                        Directory.SetLastWriteTimeUtc(entry.Path, entry.Modified);
                        Directory.SetCreationTimeUtc(entry.Path, entry.Created);
                        File.SetAttributes(entry.Path, (FileAttributes)entry.Attributes);
                    }
                    else
                    {
                        File.SetLastWriteTimeUtc(entry.Path, entry.Modified);
                        File.SetCreationTimeUtc(entry.Path, entry.Created);
                        File.SetAttributes(entry.Path, (FileAttributes)entry.Attributes);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Timestamps and attributes are best effort; content correctness is not.
                }
            }
            return count;
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private static void CopyFile(
        string path, string relative, long length, ILockContentSink sink, byte[] buffer)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, CopyBuffer, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LockFileInUseException(relative);
        }

        using (file)
        {
            long copied = 0;
            while (copied < length)
            {
                var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - copied));
                if (read <= 0)
                    throw new LockFileInUseException(relative + " (changed while reading)");
                sink.Write(buffer.AsSpan(0, read));
                copied += read;
            }
            if (file.ReadByte() != -1)
                throw new LockFileInUseException(relative + " (grew while reading)");
        }
    }

    private static void CopyExactly(Stream source, Stream destination, long length, byte[] buffer)
    {
        var remaining = length;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new LockDamagedException("archive ended early");
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteHeader(
        ILockContentSink sink, byte kind, string relative, long modifiedTicks, int attributes, long length)
    {
        var path = Encoding.UTF8.GetBytes(relative);
        if (path.Length is 0 or > 1024)
            throw new ArgumentException("The archive path is too long.", nameof(relative));
        var header = new byte[1 + 2 + path.Length + 8 + 4 + (kind == EntryFile ? 8 : 0)];
        header[0] = kind;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), (ushort)path.Length);
        path.CopyTo(header.AsSpan(3));
        var at = 3 + path.Length;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(at), modifiedTicks);
        at += 8;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at), attributes);
        at += 4;
        if (kind == EntryFile)
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(at), length);
        sink.Write(header);
    }

    private static byte ReadByte(Stream stream)
    {
        var value = stream.ReadByte();
        if (value < 0)
            throw new LockDamagedException("archive ended early");
        return (byte)value;
    }

    private static ushort ReadUInt16(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadFully(stream, buffer);
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadFully(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static long ReadInt64(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadFully(stream, buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    private static string ReadUtf8(Stream stream, int length)
    {
        var bytes = new byte[length];
        ReadFully(stream, bytes);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new LockDamagedException("archive path encoding");
        }
    }

    private static void ReadFully(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
                throw new LockDamagedException("archive ended early");
            offset += read;
        }
    }

    private static DateTime FromUtcTicks(long ticks)
    {
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            throw new LockDamagedException("archive timestamp");
        return new DateTime(ticks, DateTimeKind.Utc);
    }
}
