using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;
using ZipArchive = SharpCompress.Archives.Zip.ZipArchive;

namespace FileAction.Core;

/// <summary>
/// Writes a standard ZIP archive whose file entries are encrypted with WinZip AES-256 (AE-2). ZIP has no
/// field for encrypting entry names, so names stay readable; the UI says so explicitly.
///
/// The format is deliberately implemented here instead of adding another package: the only referenced ZIP
/// library (SharpCompress) can read AES entries but cannot write them. Everything used below is in the
/// .NET runtime. Zip64 is not implemented; a file or archive over the ZIP32 limit is refused with a clear
/// error rather than producing a broken archive.
/// </summary>
public static class AesZipWriter
{
    private const ushort AesCompressionMethod = 99;
    private const ushort AesExtraFieldId = 0x9901;
    private const ushort AesVersionAe2 = 2;
    private const byte AesStrength256 = 3;
    private const ushort DeflateCompressionMethod = 8;
    private const ushort StoredCompressionMethod = 0;
    private const ushort VersionNeeded = 51;
    private const ushort Utf8Flag = 0x0800;
    private const ushort EncryptedFlag = 0x0001;
    private const ushort StrongEncryptionFlag = 0x0040;
    private const int ChunkSize = 81920;

    /// <summary>Creates <paramref name="outputPath"/>; the file must not already exist.</summary>
    public static ZipProtectionResult Create(
        ProtectedZipPlan plan,
        string outputPath,
        string password,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (plan.Entries.Count == 0 || plan.Entries.All(entry => entry.IsDirectory))
            throw new PasswordProtectionException(PasswordProtectionFailure.Empty, "tool.protect.error.empty");

        var totalBytes = plan.Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Length);
        var container = new ZipContainer(outputPath, totalBytes, progress);
        try
        {
            foreach (var entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                container.WriteEntry(entry, password, cancellationToken);
            }

            container.Finish();
            progress?.Report(1);
            return new ZipProtectionResult(
                FileCount: plan.Entries.Count(entry => !entry.IsDirectory),
                DirectoryCount: plan.Entries.Count(entry => entry.IsDirectory),
                SkippedLinks: plan.SkippedLinks);
        }
        catch
        {
            container.Dispose();
            OutputPaths.TryDelete(outputPath);
            throw;
        }
        finally
        {
            container.Dispose();
        }
    }

    /// <summary>One ZIP container: local entries, central directory and end record.</summary>
    private sealed class ZipContainer : IDisposable
    {
        private readonly FileStream _output;
        private readonly List<CentralEntry> _central = [];
        private readonly long _totalBytes;
        private readonly IProgress<double>? _progress;
        private long _processedBytes;
        private long _lastReportedBytes;
        private bool _finished;

        public ZipContainer(string outputPath, long totalBytes, IProgress<double>? progress)
        {
            _output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            _totalBytes = Math.Max(1, totalBytes);
            _progress = progress;
            progress?.Report(0);
        }

        public void WriteEntry(ProtectedZipEntry entry, string password, CancellationToken cancellationToken)
        {
            if (entry.IsDirectory)
            {
                WriteDirectory(entry);
                return;
            }

            if (entry.Length > ProtectedZipPlanBuilder.MaximumEntrySize)
                throw new PasswordProtectionException(
                    PasswordProtectionFailure.TooLarge, "tool.protect.error.tooLarge");

            var name = EncodeName(entry.EntryName);
            var extra = BuildAesExtra();
            var timestamp = Timestamp(entry.SourcePath);
            var localOffset = _output.Position;
            if (localOffset > uint.MaxValue)
                throw TooLarge();

            // Placeholder sizes; they are patched once the encrypted payload is complete.
            WriteLocalHeader(name, extra, EncryptedFlag | StrongEncryptionFlag | Utf8Flag, AesCompressionMethod, timestamp,
                crc: 0, compressedSize: 0, uncompressedSize: 0);

            var salt = RandomNumberGenerator.GetBytes(ZipAesKeyDerivationPlan.Default.SaltBytes);
            var verifierLength = 2;
            var keyMaterial = ZipAesKeyDerivation.Derive(password, salt);
            long cipherBytes;
            byte[] authenticationCode;
            long uncompressedBytes = 0;
            try
            {
                _output.Write(salt);
                _output.Write(keyMaterial.PasswordVerifier);

                using var cipher = new AesCtrCipher(keyMaterial.EncryptionKey);
                using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, keyMaterial.AuthenticationKey);
                using var cipherStream = new AesCtrCipherStream(_output, cipher, hmac);
                using (var deflate = new DeflateStream(cipherStream, CompressionLevel.Optimal, leaveOpen: true))
                using (var source = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, FileOptions.SequentialScan))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
                    try
                    {
                        int read;
                        while ((read = source.Read(buffer, 0, ChunkSize)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            deflate.Write(buffer, 0, read);
                            uncompressedBytes = AddProgress(read);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                // Disposing the DeflateStream above flushed the final block through the cipher stream.
                cipherStream.Finish();
                authenticationCode = cipherStream.AuthenticationCode;
                cipherBytes = cipherStream.Written;
                _output.Write(authenticationCode);
            }
            finally
            {
                keyMaterial.Dispose();
            }

            var compressedSize = salt.Length + verifierLength + cipherBytes + authenticationCode.Length;
            if (compressedSize > uint.MaxValue || uncompressedBytes > uint.MaxValue)
                throw TooLarge();

            PatchLocalHeader(localOffset, crc: 0, (uint)compressedSize, (uint)uncompressedBytes);
            _central.Add(new CentralEntry(
                name, extra, EncryptedFlag | StrongEncryptionFlag | Utf8Flag, AesCompressionMethod, timestamp,
                Crc: 0, CompressedSize: (uint)compressedSize, UncompressedSize: (uint)uncompressedBytes,
                LocalOffset: (uint)localOffset, ExternalAttributes: 0));
        }

        private void WriteDirectory(ProtectedZipEntry entry)
        {
            var name = EncodeName(entry.EntryName);
            var timestamp = Timestamp(entry.SourcePath);
            var localOffset = _output.Position;
            if (localOffset > uint.MaxValue)
                throw TooLarge();

            WriteLocalHeader(name, [], Utf8Flag, StoredCompressionMethod, timestamp, 0, 0, 0);
            _central.Add(new CentralEntry(
                name, [], Utf8Flag, StoredCompressionMethod, timestamp,
                Crc: 0, CompressedSize: 0, UncompressedSize: 0,
                LocalOffset: (uint)localOffset, ExternalAttributes: 0x10));
        }

        public void Finish()
        {
            if (_finished)
                return;
            _finished = true;

            var centralOffset = _output.Position;
            if (centralOffset > uint.MaxValue)
                throw TooLarge();

            foreach (var entry in _central)
            {
                WriteU32(0x02014b50);
                WriteU16(VersionNeeded);
                WriteU16(VersionNeeded);
                WriteU16(entry.Flags);
                WriteU16(entry.Method);
                WriteU16(entry.Timestamp.Time);
                WriteU16(entry.Timestamp.Date);
                WriteU32(entry.Crc);
                WriteU32(entry.CompressedSize);
                WriteU32(entry.UncompressedSize);
                WriteU16((ushort)entry.Name.Length);
                WriteU16((ushort)entry.Extra.Length);
                WriteU16(0);
                WriteU16(0);
                WriteU16(0);
                WriteU32(entry.ExternalAttributes);
                WriteU32(entry.LocalOffset);
                _output.Write(entry.Name);
                _output.Write(entry.Extra);
            }

            var centralSize = _output.Position - centralOffset;
            if (centralSize > uint.MaxValue || _central.Count > ushort.MaxValue)
                throw TooLarge();

            WriteU32(0x06054b50);
            WriteU16(0);
            WriteU16(0);
            WriteU16((ushort)_central.Count);
            WriteU16((ushort)_central.Count);
            WriteU32((uint)centralSize);
            WriteU32((uint)centralOffset);
            WriteU16(0);
            _output.Flush();
        }

        private long AddProgress(int bytes)
        {
            _processedBytes += bytes;
            _lastReportedBytes += bytes;
            if (_lastReportedBytes >= 1024 * 1024)
            {
                _lastReportedBytes = 0;
                _progress?.Report(Math.Clamp((double)_processedBytes / _totalBytes, 0, 0.999));
            }
            return _processedBytes;
        }

        private void PatchLocalHeader(long localOffset, uint crc, uint compressedSize, uint uncompressedSize)
        {
            var end = _output.Position;
            _output.Position = localOffset + 14;
            WriteU32(crc);
            WriteU32(compressedSize);
            WriteU32(uncompressedSize);
            _output.Position = end;
        }

        private void WriteLocalHeader(
            byte[] name, byte[] extra, ushort flags, ushort method, DosTimestamp timestamp,
            uint crc, uint compressedSize, uint uncompressedSize)
        {
            WriteU32(0x04034b50);
            WriteU16(VersionNeeded);
            WriteU16(flags);
            WriteU16(method);
            WriteU16(timestamp.Time);
            WriteU16(timestamp.Date);
            WriteU32(crc);
            WriteU32(compressedSize);
            WriteU32(uncompressedSize);
            WriteU16((ushort)name.Length);
            WriteU16((ushort)extra.Length);
            _output.Write(name);
            _output.Write(extra);
        }

        private void WriteU16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            _output.Write(bytes);
        }

        private void WriteU32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            _output.Write(bytes);
        }

        public void Dispose()
        {
            _output.Dispose();
        }
    }

    private sealed record CentralEntry(
        byte[] Name,
        byte[] Extra,
        ushort Flags,
        ushort Method,
        DosTimestamp Timestamp,
        uint Crc,
        uint CompressedSize,
        uint UncompressedSize,
        uint LocalOffset,
        uint ExternalAttributes);

    private readonly record struct DosTimestamp(ushort Time, ushort Date);

    private static DosTimestamp Timestamp(string path)
    {
        DateTime time;
        try
        {
            time = File.GetLastWriteTime(path);
        }
        catch
        {
            time = DateTime.Now;
        }

        if (time.Year < 1980)
            time = new DateTime(1980, 1, 1);
        if (time.Year > 2107)
            time = new DateTime(2107, 12, 31, 23, 59, 58);

        var date = (ushort)(((time.Year - 1980) << 9) | (time.Month << 5) | time.Day);
        var clock = (ushort)((time.Hour << 11) | (time.Minute << 5) | (time.Second / 2));
        return new DosTimestamp(clock, date);
    }

    private static byte[] EncodeName(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        if (bytes.Length > ushort.MaxValue)
            throw new PasswordProtectionException(
                PasswordProtectionFailure.UnsupportedInput, "tool.protect.error.unsupported");
        return bytes;
    }

    private static byte[] BuildAesExtra()
    {
        var extra = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(0), AesExtraFieldId);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(4), AesVersionAe2);
        extra[6] = (byte)'A';
        extra[7] = (byte)'E';
        extra[8] = AesStrength256;
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(9), DeflateCompressionMethod);
        return extra;
    }

    private static PasswordProtectionException TooLarge() =>
        new(PasswordProtectionFailure.TooLarge, "tool.protect.error.tooLarge");

    /// <summary>AES-CTR as WinZip defines it: AES-ECB over a 16-byte little-endian counter starting at 1.</summary>
    private sealed class AesCtrCipher : IDisposable
    {
        private readonly Aes _aes;
        private readonly ICryptoTransform _encryptor;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _keyStream = new byte[16];
        private int _position = 16;

        public AesCtrCipher(byte[] key)
        {
            _aes = Aes.Create();
            _aes.Key = key;
            _aes.Mode = CipherMode.ECB;
            _aes.Padding = PaddingMode.None;
            _encryptor = _aes.CreateEncryptor();
        }

        public void Transform(ReadOnlySpan<byte> input, Span<byte> output)
        {
            for (var i = 0; i < input.Length; i++)
            {
                if (_position == 16)
                    NextBlock();
                output[i] = (byte)(input[i] ^ _keyStream[_position++]);
            }
        }

        private void NextBlock()
        {
            for (var i = 0; i < _counter.Length; i++)
            {
                _counter[i]++;
                if (_counter[i] != 0)
                    break;
            }

            _encryptor.TransformBlock(_counter, 0, _counter.Length, _keyStream, 0);
            _position = 0;
        }

        public void Dispose()
        {
            _encryptor.Dispose();
            _aes.Dispose();
            CryptographicOperations.ZeroMemory(_counter);
            CryptographicOperations.ZeroMemory(_keyStream);
        }
    }

    /// <summary>Encrypts what the deflate stream writes and authenticates the ciphertext in one pass.</summary>
    private sealed class AesCtrCipherStream : Stream
    {
        private readonly Stream _output;
        private readonly AesCtrCipher _cipher;
        private readonly IncrementalHash _hmac;
        private byte[]? _authenticationCode;

        public AesCtrCipherStream(Stream output, AesCtrCipher cipher, IncrementalHash hmac)
        {
            _output = output;
            _cipher = cipher;
            _hmac = hmac;
        }

        public long Written { get; private set; }

        public void Finish() => _authenticationCode ??= _hmac.GetHashAndReset()[..10];

        public byte[] AuthenticationCode =>
            _authenticationCode ?? throw new InvalidOperationException("The entry has not been finished.");

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length == 0)
                return;

            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var ciphertext = rented.AsSpan(0, buffer.Length);
                _cipher.Transform(buffer, ciphertext);
                _hmac.AppendData(ciphertext);
                _output.Write(ciphertext);
                Written += buffer.Length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Flush() => _output.Flush();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Written; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>
/// Removes password protection from a ZIP by writing a new, unencrypted archive. It makes exactly one
/// attempt with the typed password and never tries to recover or guess one.
/// </summary>
public static class ZipPasswordUnlocker
{
    public static ZipUnlockResult Unlock(
        string sourcePath,
        string outputPath,
        string password,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var outputCreated = false;
        try
        {
            using var archive = ArchiveFactory.OpenArchive(sourcePath, new ReaderOptions { Password = password });
            var fileEntries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
            if (fileEntries.Count == 0)
                throw new PasswordProtectionException(PasswordProtectionFailure.Empty, "tool.unlock.error.empty");
            if (!fileEntries.Any(entry => entry.IsEncrypted))
                throw new PasswordProtectionException(PasswordProtectionFailure.NotEncrypted, "tool.unlock.error.notEncrypted");

            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            outputCreated = true;
            using var writer = new ZipWriter(
                output,
                new ZipWriterOptions(CompressionType.Deflate)
                {
                    ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 },
                });

            var done = 0;
            foreach (var entry in fileEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Key))
                    continue;
                using var raw = entry.OpenEntryStream();
                writer.Write(entry.Key, raw, new ZipWriterEntryOptions
                {
                    ModificationDateTime = entry.LastModifiedTime,
                });
                done++;
                progress?.Report((double)done / fileEntries.Count);
            }

            return new ZipUnlockResult(done);
        }
        catch (OperationCanceledException)
        {
            if (outputCreated)
                OutputPaths.TryDelete(outputPath);
            throw;
        }
        catch (PasswordProtectionException)
        {
            if (outputCreated)
                OutputPaths.TryDelete(outputPath);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (outputCreated)
                OutputPaths.TryDelete(outputPath);
            throw new PasswordProtectionException(
                PasswordProtectionFailure.WrongPasswordOrCorrupt, "tool.unlock.error.password", ex);
        }
    }
}
