using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>The three ways into a protected file; the number is stored in the slot.</summary>
public enum LockSecretKind : byte
{
    FilePassword = 1,
    RescuerPassword = 2,
    RescuerKey = 3,
}

/// <summary>Why a protected file could not be opened. Never names the secret that was tried.</summary>
public enum LockFailure
{
    /// <summary>None of the slots accepted the supplied secret.</summary>
    WrongSecret,

    /// <summary>The file does not start with the DropWheel lock magic.</summary>
    NotProtected,

    /// <summary>The header is structurally invalid or its authentication code does not verify.</summary>
    Damaged,

    /// <summary>The file ends before its final frame; it was not written completely.</summary>
    Truncated,

    /// <summary>The container version or a slot kind is newer than this build understands.</summary>
    Unsupported,
}

/// <summary>Base type for every controlled failure while opening a protected file.</summary>
public abstract class LockException(string message) : Exception(message);

public sealed class LockWrongSecretException()
    : LockException("The password or key does not open this file.");

public sealed class LockDamagedException(string detail)
    : LockException($"This protected file is damaged and cannot be opened ({detail}).");

public sealed class LockTruncatedException()
    : LockException("This protected file is incomplete: it ends before its last verified block.");

public sealed class LockNotProtectedException()
    : LockException("This is not a DropWheel protected file.");

public sealed class LockUnsupportedException(string detail)
    : LockException($"This protected file uses a newer format that this build cannot open ({detail}).");

/// <summary>One wrapped copy of the file's data key.</summary>
public sealed class LockSlot
{
    public const int Length = 84;

    public LockSecretKind Kind { get; init; }

    /// <summary>Per-slot random salt for the password/key derivation.</summary>
    public byte[] Salt { get; init; } = new byte[LockFormat.SaltLength];

    /// <summary>PBKDF2 work factor stored with the slot, so an old file stays openable.</summary>
    public uint Iterations { get; init; }

    public byte[] Nonce { get; init; } = new byte[LockFormat.NonceLength];
    public byte[] WrappedKey { get; init; } = new byte[LockFormat.KeyLength];
    public byte[] Tag { get; init; } = new byte[LockFormat.TagLength];

    public LockSlot Clone() => new()
    {
        Kind = Kind,
        Salt = (byte[])Salt.Clone(),
        Iterations = Iterations,
        Nonce = (byte[])Nonce.Clone(),
        WrappedKey = (byte[])WrappedKey.Clone(),
        Tag = (byte[])Tag.Clone(),
    };
}

/// <summary>
/// The authenticated header at the start of every protected file. Its size is fixed, so a slot rewrite
/// never changes the payload offset; the authentication code covers every header byte and proves, once a
/// data key is known, that nothing in the header was altered.
/// </summary>
public sealed class LockHeader
{
    public const int Length = LockFormat.HeaderLength;

    public byte[] FileId { get; init; } = new byte[LockFormat.FileIdLength];
    public ushort Flags { get; init; }

    /// <summary>Exactly three slots, in the order file password, rescuer password, rescuer key.</summary>
    public LockSlot[] Slots { get; init; } = new LockSlot[3];

    /// <summary>HMAC-SHA-256 over the header prefix with the data key derived MAC key.</summary>
    public byte[] Mac { get; init; } = new byte[LockFormat.MacLength];

    public bool IsDirectory => (Flags & LockFormat.FlagDirectory) != 0;

    public byte[] SerializeWithoutMac()
    {
        var buffer = new byte[Length - LockFormat.MacLength];
        var span = buffer.AsSpan();
        span.Clear();
        FileId.AsSpan().CopyTo(span[..LockFormat.FileIdLength]);
        span[16] = (byte)Slots.Length;
        // 17..19 reserved.
        for (var i = 0; i < Slots.Length; i++)
        {
            var slot = Slots[i];
            var at = span[(20 + (i * LockSlot.Length))..];
            at[0] = (byte)slot.Kind;
            at[1] = at[2] = at[3] = 0;
            slot.Salt.AsSpan().CopyTo(at[4..20]);
            BinaryPrimitives.WriteUInt32LittleEndian(at[20..24], slot.Iterations);
            slot.Nonce.AsSpan().CopyTo(at[24..36]);
            slot.WrappedKey.AsSpan().CopyTo(at[36..68]);
            slot.Tag.AsSpan().CopyTo(at[68..84]);
        }
        return buffer;
    }

    public byte[] Serialize()
    {
        var buffer = new byte[Length];
        SerializeWithoutMac().CopyTo(buffer, 0);
        Mac.CopyTo(buffer, Length - LockFormat.MacLength);
        return buffer;
    }

    public static LockHeader Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length != Length)
            throw new LockDamagedException("header size");

        var count = header[16];
        if (count != 3)
            throw new LockUnsupportedException($"slot count {count}");

        var slots = new LockSlot[3];
        for (var i = 0; i < slots.Length; i++)
        {
            var at = header[(20 + (i * LockSlot.Length))..];
            var kind = (LockSecretKind)at[0];
            if (kind is not (LockSecretKind.FilePassword or LockSecretKind.RescuerPassword or LockSecretKind.RescuerKey))
                throw new LockUnsupportedException($"slot kind {(byte)at[0]}");
            if (i > 0 && (byte)kind != i + 1)
                throw new LockDamagedException("slot order");

            slots[i] = new LockSlot
            {
                Kind = kind,
                Salt = at[4..20].ToArray(),
                Iterations = BinaryPrimitives.ReadUInt32LittleEndian(at[20..24]),
                Nonce = at[24..36].ToArray(),
                WrappedKey = at[36..68].ToArray(),
                Tag = at[68..84].ToArray(),
            };
        }

        return new LockHeader
        {
            FileId = header[..LockFormat.FileIdLength].ToArray(),
            Flags = 0,
            Slots = slots,
            Mac = header[(Length - LockFormat.MacLength)..].ToArray(),
        };
    }

    /// <summary>The bytes that bind payload frames to this file, independent of its slots.</summary>
    public byte[] StableAad() => LockFormat.BuildStableAad(FileId, Flags);
}

/// <summary>Everything the encrypted metadata block stores about the original item.</summary>
public sealed record LockMeta
{
    public const byte MetaVersion = 1;
    public const int MaxNameBytes = 4096;
    public const int MaxDesktopIniBytes = 1024 * 1024;

    public byte FormatVersion { get; init; } = MetaVersion;
    public bool IsDirectory { get; init; }

    /// <summary>The original file or folder name, extension included, as it must be restored.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Original size in bytes for a file; 0 for a folder.</summary>
    public long Size { get; init; }

    public long CreationTimeUtcTicks { get; init; }
    public long LastWriteTimeUtcTicks { get; init; }
    public long LastAccessTimeUtcTicks { get; init; }

    /// <summary>Original <see cref="FileAttributes"/>; only the low 16 bits are meaningful.</summary>
    public int Attributes { get; init; }

    /// <summary>The contents of an existing <c>desktop.ini</c> before we replaced it, or null.</summary>
    public byte[]? PreviousDesktopIni { get; init; }

    /// <summary>The attributes of that previous desktop.ini, restored with its bytes.</summary>
    public int PreviousDesktopIniAttributes { get; init; }

    /// <summary>How many files and folders the encrypted archive carries.</summary>
    public int EntryCount { get; init; }

    public static LockMeta ForFile(string path)
    {
        var info = new FileInfo(path);
        return new LockMeta
        {
            IsDirectory = false,
            Name = info.Name,
            Size = info.Length,
            CreationTimeUtcTicks = info.CreationTimeUtc.Ticks,
            LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
            LastAccessTimeUtcTicks = info.LastAccessTimeUtc.Ticks,
            Attributes = (int)info.Attributes & 0xFFFF,
        };
    }

    public static LockMeta ForDirectory(string path, int entryCount, byte[]? previousDesktopIni)
    {
        var info = new DirectoryInfo(path);
        return new LockMeta
        {
            IsDirectory = true,
            Name = info.Name,
            Size = 0,
            CreationTimeUtcTicks = info.CreationTimeUtc.Ticks,
            LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
            LastAccessTimeUtcTicks = info.LastAccessTimeUtc.Ticks,
            Attributes = (int)info.Attributes & 0xFFFF,
            EntryCount = entryCount,
            PreviousDesktopIni = previousDesktopIni,
        };
    }

    /// <summary>Throws when a value read out of an archive could escape the restore folder.</summary>
    public void Validate()
    {
        if (FormatVersion != MetaVersion)
            throw new LockUnsupportedException($"metadata version {FormatVersion}");
        if (string.IsNullOrEmpty(Name) || Name.Length > 255)
            throw new LockDamagedException("name");
        if (Name is "." or ".." || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new LockDamagedException("name characters");
        if (Size < 0)
            throw new LockDamagedException("size");
        if (PreviousDesktopIni is { Length: > MaxDesktopIniBytes })
            throw new LockDamagedException("desktop.ini size");
    }

    public byte[] Serialize()
    {
        var name = Encoding.UTF8.GetBytes(Name);
        if (name.Length is 0 or > MaxNameBytes)
            throw new LockDamagedException("name length");
        var desktop = PreviousDesktopIni ?? [];
        if (desktop.Length > MaxDesktopIniBytes)
            throw new LockDamagedException("desktop.ini length");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write((byte)(IsDirectory ? 1 : 0));
        writer.Write((ushort)name.Length);
        writer.Write(name);
        writer.Write(Size);
        writer.Write(CreationTimeUtcTicks);
        writer.Write(LastWriteTimeUtcTicks);
        writer.Write(LastAccessTimeUtcTicks);
        writer.Write(Attributes);
        writer.Write((byte)(PreviousDesktopIni is null ? 0 : 1));
        writer.Write(desktop.Length);
        writer.Write(desktop);
        writer.Write(PreviousDesktopIniAttributes);
        writer.Write(EntryCount);
        writer.Flush();
        return stream.ToArray();
    }

    public static LockMeta Deserialize(ReadOnlySpan<byte> data)
    {
        var reader = new SpanReader(data);
        var version = reader.ReadByte();
        if (version != MetaVersion)
            throw new LockUnsupportedException($"metadata version {version}");
        var directory = reader.ReadByte() != 0;
        var nameLength = reader.ReadUInt16();
        if (nameLength is 0 or > MaxNameBytes)
            throw new LockDamagedException("name length");
        var name = reader.ReadUtf8(nameLength);
        var size = reader.ReadInt64();
        var created = reader.ReadInt64();
        var modified = reader.ReadInt64();
        var accessed = reader.ReadInt64();
        var attributes = reader.ReadInt32();
        var hasDesktop = reader.ReadByte() != 0;
        var desktopLength = reader.ReadInt32();
        if (desktopLength < 0 || desktopLength > MaxDesktopIniBytes)
            throw new LockDamagedException("desktop.ini length");
        var desktop = reader.ReadBytes(desktopLength);
        var desktopAttributes = reader.ReadInt32();
        var entries = reader.ReadInt32();
        reader.EnsureEnd();

        var meta = new LockMeta
        {
            FormatVersion = version,
            IsDirectory = directory,
            Name = name,
            Size = size,
            CreationTimeUtcTicks = created,
            LastWriteTimeUtcTicks = modified,
            LastAccessTimeUtcTicks = accessed,
            Attributes = attributes,
            PreviousDesktopIni = hasDesktop ? desktop : null,
            PreviousDesktopIniAttributes = desktopAttributes,
            EntryCount = entries,
        };
        meta.Validate();
        return meta;
    }
}

/// <summary>What a completed decryption streamed, used to prove it matches the stored values.</summary>
public readonly record struct LockDigest(long Length, byte[] Sha256);

/// <summary>Format constants, header layout and fixed buffers shared by every lock operation.</summary>
public static class LockFormat
{
    /// <summary>Extension of an encrypted file.</summary>
    public const string FileExtension = ".dwenc";

    /// <summary>Extension of an encrypted folder.</summary>
    public const string FolderExtension = ".dwdir";

    /// <summary>Template-level file name used while a container is being written.</summary>
    public const string TempSuffix = ".tmp";

    public const ushort Version = 1;
    public const ushort FlagDirectory = 1;
    public const int FileIdLength = 16;
    public const int SaltLength = 16;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int KeyLength = 32;
    public const int MacLength = 32;

    /// <summary>Magic + version + flags + header length.</summary>
    public const int PrefixLength = 8 + 2 + 2 + 4;

    public const int HeaderLength = FileIdLength + 4 + (3 * LockSlot.Length) + MacLength;

    /// <summary>Payload chunk size. Every frame is authenticated on its own, so memory stays flat.</summary>
    public const int ChunkSize = 1024 * 1024;

    /// <summary>Frame prefix: kind, plaintext length, nonce.</summary>
    public const int FrameHeaderLength = 1 + 4 + NonceLength;

    public const int FooterLength = 8 + 32;

    /// <summary>Cap for the encrypted metadata frame (name, timestamps, desktop.ini backup).</summary>
    public const int MaxMetaFrame = 4 * 1024 * 1024;

    /// <summary>PBKDF2-SHA256 work factor required for password slots (OWASP 2023 guidance).</summary>
    public const int MinimumIterations = 600_000;

    /// <summary>Work factor used for new password slots.</summary>
    public const int DefaultIterations = 600_000;

    /// <summary>Work factor used for the high-entropy rescuer key slot.</summary>
    public const int KeyIterations = 200_000;

    public const byte FrameMeta = 0;
    public const byte FrameContent = 1;
    public const byte FrameFooter = 2;

    private static ReadOnlySpan<byte> MagicBytes => "DWLOCK1\0"u8;

    public static byte[] BuildStableAad(ReadOnlySpan<byte> fileId, ushort flags)
    {
        var aad = new byte[8 + 2 + 2 + FileIdLength];
        MagicBytes.CopyTo(aad);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(8), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(aad.AsSpan(10), flags);
        fileId.CopyTo(aad.AsSpan(12));
        return aad;
    }

    public static byte[] BuildPrefix(ushort flags, int headerLength)
    {
        var prefix = new byte[PrefixLength];
        MagicBytes.CopyTo(prefix);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(8), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(10), flags);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.AsSpan(12), headerLength);
        return prefix;
    }

    public static bool HasMagic(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 8 && prefix[..8].SequenceEqual(MagicBytes);

    public static ushort ReadVersion(ReadOnlySpan<byte> prefix) => BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..]);

    public static ushort ReadFlags(ReadOnlySpan<byte> prefix) => BinaryPrimitives.ReadUInt16LittleEndian(prefix[10..]);

    public static int ReadHeaderLength(ReadOnlySpan<byte> prefix) => BinaryPrimitives.ReadInt32LittleEndian(prefix[12..]);

    public static bool IsProtectedName(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(FileExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(FolderExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedFolderName(string path) =>
        Path.GetExtension(path).Equals(FolderExtension, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Bounds-checked little-endian reader for the metadata and archive blocks.</summary>
internal ref struct SpanReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _offset;

    public byte ReadByte()
    {
        Need(1);
        return _data[_offset++];
    }

    public ushort ReadUInt16()
    {
        Need(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[_offset..]);
        _offset += 2;
        return value;
    }

    public int ReadInt32()
    {
        Need(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
        _offset += 4;
        return value;
    }

    public long ReadInt64()
    {
        Need(8);
        var value = BinaryPrimitives.ReadInt64LittleEndian(_data[_offset..]);
        _offset += 8;
        return value;
    }

    public byte[] ReadBytes(int count)
    {
        if (count < 0)
            throw new LockDamagedException("negative length");
        Need(count);
        var value = _data.Slice(_offset, count).ToArray();
        _offset += count;
        return value;
    }

    public string ReadUtf8(int count)
    {
        var bytes = ReadBytes(count);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new LockDamagedException("text encoding");
        }
    }

    public void EnsureEnd()
    {
        if (_offset != _data.Length)
            throw new LockDamagedException("trailing metadata");
    }

    private void Need(int count)
    {
        if (count > _data.Length - _offset)
            throw new LockDamagedException("metadata bounds");
    }
}
