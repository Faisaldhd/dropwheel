using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileAction.Core.Lock;

/// <summary>All secrets and work factors needed to protect one file.</summary>
public sealed class LockCredentials
{
    public required string FilePassword { get; init; }

    /// <summary>The Rescuer password: one password that opens every file the user protects.</summary>
    public required string RescuerPassword { get; init; }

    /// <summary>The Rescuer key in canonical form (see <see cref="RescuerKey"/>).</summary>
    public required string RescuerKey { get; init; }

    public int FilePasswordIterations { get; init; } = LockFormat.DefaultIterations;
    public int RescuerPasswordIterations { get; init; } = LockFormat.DefaultIterations;
    public int RescuerKeyIterations { get; init; } = LockFormat.KeyIterations;

    public void Validate()
    {
        if (!LockSecrets.IsAcceptablePassword(FilePassword))
            throw new ArgumentException("The file password is too short.", nameof(FilePassword));
        if (!LockSecrets.IsAcceptablePassword(RescuerPassword))
            throw new ArgumentException("The Rescuer password is too short.", nameof(RescuerPassword));
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(RescuerKey, out _))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(RescuerKey));
        if (FilePasswordIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(FilePasswordIterations));
        if (RescuerPasswordIterations < 1)
            throw new ArgumentOutOfRangeException(nameof(RescuerPasswordIterations));
    }
}

/// <summary>The result of writing a protected file.</summary>
public readonly record struct LockProtectResult(LockHeader Header, LockDigest Digest, int EntryCount = 0);

/// <summary>The result of opening one, and which slot accepted the secret.</summary>
public sealed class LockReadResult
{
    public required LockMeta Meta { get; init; }
    public required LockDigest Digest { get; init; }
    public required LockSecretKind OpenedWith { get; init; }
}

/// <summary>Receives plaintext as it is produced; the container frames it in 1 MiB blocks.</summary>
public interface ILockContentSink
{
    void Write(ReadOnlySpan<byte> data);
}

/// <summary>
/// The protected-file container: a fixed authenticated header that wraps one random data key three times,
/// then independently authenticated 1 MiB AES-256-GCM frames. Reading is streaming and never needs more
/// than one frame of memory; the trailing frame carries the plaintext length and SHA-256 so a truncated or
/// altered file is refused instead of restored.
/// </summary>
public static class LockContainer
{
    private const string HeaderMacInfo = "DropWheel-Lock-Header-v1";
    private const int MaxIterations = 10_000_000;

    // ------------------------------------------------------------------ header

    public static LockHeader CreateHeader(byte[] dataKey, LockCredentials credentials, bool directory)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();
        if (dataKey.Length != LockFormat.KeyLength)
            throw new ArgumentException("A data key is 32 bytes.", nameof(dataKey));

        var flags = directory ? LockFormat.FlagDirectory : (ushort)0;
        var fileId = RandomNumberGenerator.GetBytes(LockFormat.FileIdLength);
        var stable = LockFormat.BuildStableAad(fileId, flags);

        var slots = new[]
        {
            WrapSlot(dataKey, stable, 0, LockSecretKind.FilePassword, credentials.FilePassword,
                credentials.FilePasswordIterations),
            WrapSlot(dataKey, stable, 1, LockSecretKind.RescuerPassword, credentials.RescuerPassword,
                credentials.RescuerPasswordIterations),
            WrapSlot(dataKey, stable, 2, LockSecretKind.RescuerKey, CanonicalKey(credentials.RescuerKey),
                credentials.RescuerKeyIterations),
        };

        var header = new LockHeader { FileId = fileId, Flags = flags, Slots = slots };
        return new LockHeader
        {
            FileId = header.FileId,
            Flags = header.Flags,
            Slots = header.Slots,
            Mac = ComputeHeaderMac(dataKey, header),
        };
    }

    /// <summary>Writes the prefix and the authenticated header; the payload frames follow.</summary>
    public static void WriteHeader(Stream destination, LockHeader header)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(header);
        destination.Write(LockFormat.BuildPrefix(header.Flags, LockFormat.HeaderLength));
        destination.Write(header.Serialize());
    }

    /// <summary>Reads and structurally validates the prefix and header; it does not prove authenticity.</summary>
    public static LockHeader ReadHeader(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var prefix = new byte[LockFormat.PrefixLength];
        ReadExactly(source, prefix, allowTruncated: false);
        if (!LockFormat.HasMagic(prefix))
            throw new LockNotProtectedException();
        if (LockFormat.ReadVersion(prefix) != LockFormat.Version)
            throw new LockUnsupportedException($"container version {LockFormat.ReadVersion(prefix)}");
        var headerLength = LockFormat.ReadHeaderLength(prefix);
        if (headerLength != LockFormat.HeaderLength)
            throw new LockUnsupportedException($"header length {headerLength}");

        var bytes = new byte[headerLength];
        ReadExactly(source, bytes, allowTruncated: true);
        var header = LockHeader.Parse(bytes);
        return new LockHeader
        {
            FileId = header.FileId,
            Flags = LockFormat.ReadFlags(prefix),
            Slots = header.Slots,
            Mac = header.Mac,
        };
    }

    /// <summary>
    /// Tries every slot the supplied text could belong to. A password candidate is tried against the file
    /// and Rescuer password slots; text shaped like a Rescuer key is also tried against the key slot. The
    /// caller learns only success or <see cref="LockWrongSecretException"/>.
    /// </summary>
    public static bool TryUnwrap(LockHeader header, string secret, out byte[] dataKey, out LockSecretKind kind)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(secret);
        dataKey = [];
        kind = default;
        if (secret.Length == 0)
            return false;

        var stable = header.StableAad();
        var passwordBytes = LockSecrets.ToBytes(secret);
        byte[]? keyBytes = RescuerKey.TryParse(secret, out var canonical) ? RescuerKey.ToSecretBytes(canonical) : null;
        try
        {
            for (var i = 0; i < header.Slots.Length; i++)
            {
                var slot = header.Slots[i];
                var source = slot.Kind == LockSecretKind.RescuerKey ? keyBytes : passwordBytes;
                if (source is null)
                    continue;
                if (TryUnwrapSlot(header, slot, i, stable, source, out var candidate))
                {
                    // A slot that opens proves the header's slots; the MAC proves the whole header.
                    VerifyHeaderMac(header, candidate);
                    dataKey = candidate;
                    kind = slot.Kind;
                    return true;
                }
            }
            return false;
        }
        finally
        {
            LockSecrets.WipeAll(passwordBytes, keyBytes);
        }
    }

    /// <summary>Unwraps and returns the data key, or throws the generic wrong-secret failure.</summary>
    public static byte[] Unwrap(LockHeader header, string secret, out LockSecretKind kind)
    {
        if (!TryUnwrap(header, secret, out var dataKey, out kind))
            throw new LockWrongSecretException();
        return dataKey;
    }

    /// <summary>
    /// Tries the one slot with the requested role. A re-entered Rescuer password or key can be checked
    /// against an existing header without deriving keys for the other two slots; the header MAC is still
    /// verified, so a slot that opens proves the whole header.
    /// </summary>
    public static bool TryUnwrapRole(LockHeader header, LockSecretKind role, string secret, out byte[] dataKey)
    {
        ArgumentNullException.ThrowIfNull(header);
        dataKey = [];
        if (string.IsNullOrEmpty(secret))
            return false;
        var index = Array.FindIndex(header.Slots, slot => slot.Kind == role);
        if (index < 0)
            return false;

        var stable = header.StableAad();
        var secretBytes = role == LockSecretKind.RescuerKey
            ? global::FileAction.Core.Lock.RescuerKey.TryParse(secret, out var canonical)
                ? global::FileAction.Core.Lock.RescuerKey.ToSecretBytes(canonical)
                : null
            : LockSecrets.ToBytes(secret);
        if (secretBytes is null)
            return false;
        try
        {
            if (!TryUnwrapSlot(header, header.Slots[index], index, stable, secretBytes, out var candidate))
                return false;
            try
            {
                VerifyHeaderMac(header, candidate);
            }
            catch
            {
                LockSecrets.Wipe(candidate);
                throw;
            }
            dataKey = candidate;
            return true;
        }
        finally
        {
            LockSecrets.Wipe(secretBytes);
        }
    }

    private static bool TryUnwrapSlot(
        LockHeader header, LockSlot slot, int index, byte[] stable, byte[] secretBytes, out byte[] dataKey)
    {
        dataKey = [];
        if (slot.Iterations is 0 or > MaxIterations)
            return false;
        var kek = DeriveKey(secretBytes, slot.Salt, (int)slot.Iterations);
        var plain = new byte[LockFormat.KeyLength];
        try
        {
            using var gcm = new AesGcm(kek, LockFormat.TagLength);
            var aad = SlotAad(stable, index);
            gcm.Decrypt(slot.Nonce, slot.WrappedKey, slot.Tag, plain, aad);
            dataKey = plain;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            LockSecrets.WipeAll(kek, dataKey.Length == 0 ? plain : null);
        }
    }

    private static LockSlot WrapSlot(
        byte[] dataKey, byte[] stable, int index, LockSecretKind kind, string secret, int iterations)
    {
        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations));
        var salt = RandomNumberGenerator.GetBytes(LockFormat.SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(LockFormat.NonceLength);
        var wrapped = new byte[LockFormat.KeyLength];
        var tag = new byte[LockFormat.TagLength];
        var secretBytes = LockSecrets.ToBytes(secret);
        var kek = DeriveKey(secretBytes, salt, iterations);
        try
        {
            using var gcm = new AesGcm(kek, LockFormat.TagLength);
            gcm.Encrypt(nonce, dataKey, wrapped, tag, SlotAad(stable, index));
        }
        finally
        {
            LockSecrets.WipeAll(secretBytes, kek);
        }

        return new LockSlot
        {
            Kind = kind,
            Salt = salt,
            Iterations = (uint)iterations,
            Nonce = nonce,
            WrappedKey = wrapped,
            Tag = tag,
        };
    }

    private static byte[] SlotAad(byte[] stable, int index)
    {
        var aad = new byte[stable.Length + 1];
        stable.CopyTo(aad, 0);
        aad[^1] = (byte)index;
        return aad;
    }

    private static byte[] DeriveKey(byte[] secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, LockFormat.KeyLength);

    private static byte[] ComputeHeaderMac(byte[] dataKey, LockHeader header)
    {
        var macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, dataKey, 32, header.FileId, HeaderMacInfoBytes());
        try
        {
            return HMACSHA256.HashData(macKey, header.SerializeWithoutMac());
        }
        finally
        {
            LockSecrets.Wipe(macKey);
        }
    }

    private static void VerifyHeaderMac(LockHeader header, byte[] dataKey)
    {
        var expected = ComputeHeaderMac(dataKey, header);
        if (!LockSecrets.SameHash(expected, header.Mac))
            throw new LockDamagedException("header authentication");
    }

    private static byte[] HeaderMacInfoBytes() => System.Text.Encoding.UTF8.GetBytes(HeaderMacInfo);

    /// <summary>Canonical form of a Rescuer key, or the text unchanged when it is not key-shaped.</summary>
    public static string CanonicalKey(string secret) =>
        RescuerKey.TryParse(secret, out var canonical) ? canonical : secret;

    /// <summary>Wraps a data key into one slot; used by header rewrites to replace a single slot.</summary>
    public static LockSlot Wrap(
        byte[] dataKey, LockHeader header, int index, LockSecretKind kind, string secret, int iterations) =>
        WrapSlot(dataKey, header.StableAad(), index, kind, CanonicalKeyFor(kind, secret), iterations);

    /// <summary>A copy of <paramref name="original"/> with different slots and a freshly computed MAC.</summary>
    public static LockHeader WithSlots(LockHeader original, byte[] dataKey, LockSlot[] slots)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Length != 3)
            throw new ArgumentException("A header has three slots.", nameof(slots));
        for (var i = 0; i < slots.Length; i++)
        {
            if ((byte)slots[i].Kind != i + 1)
                throw new ArgumentException("Slots are in the wrong order.", nameof(slots));
        }
        var header = new LockHeader
        {
            FileId = original.FileId,
            Flags = original.Flags,
            Slots = slots,
        };
        return new LockHeader
        {
            FileId = header.FileId,
            Flags = header.Flags,
            Slots = header.Slots,
            Mac = ComputeHeaderMac(dataKey, header),
        };
    }

    /// <summary>Throws <see cref="LockDamagedException"/> unless the header MAC verifies under this key.</summary>
    public static void RequireHeaderMac(LockHeader header, byte[] dataKey) => VerifyHeaderMac(header, dataKey);

    /// <summary>True when the data key unwraps this header's slots (used to verify an imported key).</summary>
    public static bool HeaderMatchesKey(LockHeader header, byte[] dataKey)
    {
        try
        {
            VerifyHeaderMac(header, dataKey);
            return true;
        }
        catch (LockDamagedException)
        {
            return false;
        }
    }

    private static string CanonicalKeyFor(LockSecretKind kind, string secret) =>
        kind == LockSecretKind.RescuerKey ? CanonicalKey(secret) : secret;

    // ------------------------------------------------------------------ payload

    /// <summary>
    /// Encrypts <paramref name="source"/> into <paramref name="destination"/>, which must be positioned
    /// after a freshly written header for the returned header. Reports plaintext bytes written.
    /// </summary>
    public static LockProtectResult Encrypt(
        Stream source,
        Stream destination,
        LockCredentials credentials,
        LockMeta meta,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var dataKey = RandomNumberGenerator.GetBytes(LockFormat.KeyLength);
        try
        {
            return EncryptContent(destination, dataKey, credentials, meta, sink =>
            {
                var buffer = new byte[256 * 1024];
                try
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = source.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;
                        sink.Write(buffer.AsSpan(0, read));
                    }
                }
                finally
                {
                    Array.Clear(buffer);
                }
            }, progress, cancellationToken);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>
    /// Encrypts content produced by <paramref name="writeContent"/> into frames as it arrives, so a folder
    /// archive is never materialised as a second plaintext file. The caller owns and must wipe the key.
    /// </summary>
    public static LockProtectResult EncryptContent(
        Stream destination,
        byte[] dataKey,
        LockCredentials credentials,
        LockMeta meta,
        Action<ILockContentSink> writeContent,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return EncryptContent(destination, dataKey, CreateHeader(dataKey, credentials, meta.IsDirectory), meta,
            writeContent, progress, cancellationToken);
    }

    /// <summary>
    /// Encrypts with an existing header (an auto-lock relock keeps the file's original slots and key, so the
    /// other two secrets never have to be typed again).
    /// </summary>
    public static LockProtectResult EncryptContent(
        Stream destination,
        byte[] dataKey,
        LockHeader header,
        LockMeta meta,
        Action<ILockContentSink> writeContent,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(writeContent);
        if (meta.IsDirectory != header.IsDirectory)
            throw new ArgumentException("The header and metadata disagree about the item kind.", nameof(meta));
        meta.Validate();
        RequireHeaderMac(header, dataKey);

        WriteHeader(destination, header);
        var stable = header.StableAad();
        var frameIndex = 0UL;
        WriteFrame(destination, LockFormat.FrameMeta, frameIndex++, meta.Serialize(), dataKey, stable);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sink = new FrameSink(destination, dataKey, stable, frameIndex, hash, progress);
        writeContent(sink);
        sink.Flush();
        cancellationToken.ThrowIfCancellationRequested();

        var digest = new LockDigest(sink.Total, hash.GetHashAndReset());
        var footer = new byte[LockFormat.FooterLength];
        BinaryPrimitives.WriteInt64LittleEndian(footer, digest.Length);
        digest.Sha256.CopyTo(footer.AsSpan(8));
        WriteFrame(destination, LockFormat.FrameFooter, sink.NextFrameIndex, footer, dataKey, stable);
        return new LockProtectResult(header, digest);
    }

    private sealed class FrameSink : ILockContentSink
    {
        private readonly Stream _destination;
        private readonly byte[] _dataKey;
        private readonly byte[] _stable;
        private readonly IncrementalHash _hash;
        private readonly IProgress<long>? _progress;
        private readonly byte[] _buffer = new byte[LockFormat.ChunkSize];
        private int _filled;
        private ulong _frameIndex;

        public FrameSink(
            Stream destination, byte[] dataKey, byte[] stable, ulong frameIndex,
            IncrementalHash hash, IProgress<long>? progress)
        {
            _destination = destination;
            _dataKey = dataKey;
            _stable = stable;
            _frameIndex = frameIndex;
            _hash = hash;
            _progress = progress;
        }

        public long Total { get; private set; }

        /// <summary>The index the footer must use after every content frame has been written.</summary>
        public ulong NextFrameIndex => _frameIndex;

        public void Write(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                var take = Math.Min(_buffer.Length - _filled, data.Length);
                data[..take].CopyTo(_buffer.AsSpan(_filled));
                _filled += take;
                data = data[take..];
                if (_filled == _buffer.Length)
                    Drain();
            }
        }

        public void Flush() => Drain();

        private void Drain()
        {
            if (_filled == 0)
                return;
            var chunk = _buffer.AsSpan(0, _filled).ToArray();
            _hash.AppendData(chunk);
            WriteFrame(_destination, LockFormat.FrameContent, _frameIndex++, chunk, _dataKey, _stable);
            Total += chunk.Length;
            _progress?.Report(Total);
            Array.Clear(_buffer, 0, _filled);
            _filled = 0;
        }
    }

    /// <summary>Opens with any one of the three secrets and streams the plaintext into <paramref name="destination"/>.</summary>
    public static LockReadResult Decrypt(
        Stream source,
        Stream? destination,
        string secret,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var header = ReadHeader(source);
        var dataKey = Unwrap(header, secret, out var kind);
        try
        {
            return DecryptWithDataKey(source, destination, header, dataKey, kind, progress, cancellationToken);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>
    /// Decrypts with an already unwrapped key. The protect pipeline uses it to test-read its own temp file
    /// without paying the password derivation again.
    /// </summary>
    public static LockReadResult DecryptWithDataKey(
        Stream source,
        Stream? destination,
        LockHeader header,
        byte[] dataKey,
        LockSecretKind openedWith = LockSecretKind.FilePassword,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(dataKey);

        var stable = header.StableAad();
        var metaData = ReadFrame(source, 0, LockFormat.FrameMeta, stable, dataKey, LockFormat.MaxMetaFrame);
        var meta = LockMeta.Deserialize(metaData);
        if (meta.IsDirectory != header.IsDirectory)
            throw new LockDamagedException("meta flags");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        long footerLength = -1;
        byte[] footerHash = [];
        var index = 1UL;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ReadFrameKind(source);
            if (kind == LockFormat.FrameFooter)
            {
                var footer = ReadFrameBody(source, kind, index, stable, dataKey, LockFormat.FooterLength);
                footerLength = BinaryPrimitives.ReadInt64LittleEndian(footer);
                footerHash = footer[8..].ToArray();
                index++;
                break;
            }
            if (kind != LockFormat.FrameContent)
                throw new LockDamagedException("frame order");

            var chunk = ReadFrameBody(source, kind, index, stable, dataKey, LockFormat.ChunkSize);
            hash.AppendData(chunk);
            destination?.Write(chunk);
            total += chunk.Length;
            Array.Clear(chunk);
            progress?.Report(total);
            index++;
        }

        if (source.ReadByte() != -1)
            throw new LockDamagedException("trailing bytes");
        if (footerLength != total || footerHash.Length != 32)
            throw new LockDamagedException("length footer");
        var computed = hash.GetHashAndReset();
        if (!LockSecrets.SameHash(computed, footerHash))
            throw new LockDamagedException("content hash");
        if (!meta.IsDirectory && meta.Size != total)
            throw new LockDamagedException("size footer");

        return new LockReadResult
        {
            Meta = meta,
            Digest = new LockDigest(total, computed),
            OpenedWith = openedWith,
        };
    }

    /// <summary>Reads only the encrypted metadata block; the stream must sit just after the header.</summary>
    public static LockMeta ReadMeta(Stream source, LockHeader header, byte[] dataKey) =>
        LockMeta.Deserialize(ReadFrame(source, 0, LockFormat.FrameMeta, header.StableAad(), dataKey,
            LockFormat.MaxMetaFrame));

    /// <summary>Reads the header, then streams the whole payload to a hash sink and checks it.</summary>
    public static LockReadResult Verify(Stream source, byte[] dataKey, CancellationToken cancellationToken = default)
    {
        var header = ReadHeader(source);
        return DecryptWithDataKey(source, null, header, dataKey, LockSecretKind.FilePassword, null, cancellationToken);
    }

    /// <summary>Full verification with a user secret; used by "update Rescuer on my protected files".</summary>
    public static LockReadResult Verify(Stream source, string secret, CancellationToken cancellationToken = default) =>
        Decrypt(source, null, secret, null, cancellationToken);

    /// <summary>Cheap sanity check used before an atomic replace: the header parses and the key opens it.</summary>
    public static LockSecretKind Probe(Stream source, string secret)
    {
        var header = ReadHeader(source);
        var key = Unwrap(header, secret, out var kind);
        LockSecrets.Wipe(key);
        return kind;
    }

    /// <summary>Reads and authenticates one whole frame, for callers that already know its kind.</summary>
    public static byte[] ReadFrame(
        Stream source, ulong index, byte kind, byte[] stableAad, byte[] dataKey, int maxLength)
    {
        var actual = ReadFrameKind(source);
        if (actual != kind)
            throw new LockDamagedException("frame kind");
        return ReadFrameBody(source, kind, index, stableAad, dataKey, maxLength);
    }

    /// <summary>Writes one authenticated frame.</summary>
    public static void WriteFrame(
        Stream destination, byte kind, ulong index, byte[] plaintext, byte[] dataKey, byte[] stableAad)
    {
        var nonce = RandomNumberGenerator.GetBytes(LockFormat.NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[LockFormat.TagLength];
        using var gcm = new AesGcm(dataKey, LockFormat.TagLength);
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, FrameAad(stableAad, index, kind));
        Array.Clear(plaintext); // the caller never needs the frame plaintext again

        Span<byte> prefix = stackalloc byte[LockFormat.FrameHeaderLength];
        prefix[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(prefix[1..], plaintext.Length);
        nonce.CopyTo(prefix[5..]);
        destination.Write(prefix);
        destination.Write(ciphertext);
        destination.Write(tag);
    }

    private static byte ReadFrameKind(Stream source)
    {
        var one = new byte[1];
        var read = source.Read(one, 0, 1);
        if (read == 0)
            throw new LockTruncatedException();
        if (one[0] > LockFormat.FrameFooter)
            throw new LockDamagedException("frame kind");
        return one[0];
    }

    private static byte[] ReadFrameBody(
        Stream source, byte kind, ulong index, byte[] stableAad, byte[] dataKey, int maxLength)
    {
        var header = new byte[4 + LockFormat.NonceLength];
        ReadExactly(source, header, allowTruncated: true);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > maxLength)
            throw new LockDamagedException("frame length");
        var nonce = header[4..];
        var ciphertext = new byte[length];
        var tag = new byte[LockFormat.TagLength];
        ReadExactly(source, ciphertext, allowTruncated: true);
        ReadExactly(source, tag, allowTruncated: true);
        var plaintext = new byte[length];
        try
        {
            using var gcm = new AesGcm(dataKey, LockFormat.TagLength);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext, FrameAad(stableAad, index, kind));
        }
        catch (CryptographicException)
        {
            throw new LockDamagedException("frame authentication");
        }
        finally
        {
            Array.Clear(ciphertext);
        }
        return plaintext;
    }

    private static byte[] FrameAad(byte[] stableAad, ulong index, byte kind)
    {
        var aad = new byte[stableAad.Length + 8 + 1];
        stableAad.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(stableAad.Length), index);
        aad[^1] = kind;
        return aad;
    }

    /// <summary>Reads exactly <paramref name="buffer"/> or fails with the right lock exception.</summary>
    public static void ReadExactly(Stream source, byte[] buffer, bool allowTruncated)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = source.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                if (allowTruncated)
                    throw new LockTruncatedException();
                throw new LockDamagedException("premature end of file");
            }
            offset += read;
        }
    }
}
