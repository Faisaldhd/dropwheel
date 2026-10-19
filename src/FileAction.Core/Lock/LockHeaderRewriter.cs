using System.IO;

namespace FileAction.Core.Lock;

/// <summary>
/// The handful of file operations a header rewrite performs, isolated so a test can fail any one of them
/// and prove the original file is still openable with the old secrets.
/// </summary>
public interface ILockFileOps
{
    Stream OpenRead(string path);
    Stream CreateNew(string path);
    void FlushToDisk(Stream stream);
    void Replace(string temporaryPath, string destinationPath);
    void Delete(string path);
    bool Exists(string path);
}

/// <summary>The real file system, with a durable flush before every replace.</summary>
public sealed class RealLockFileOps : ILockFileOps
{
    public static readonly RealLockFileOps Instance = new();

    public Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024,
            FileOptions.SequentialScan);

    public Stream CreateNew(string path) =>
        new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 256 * 1024,
            FileOptions.SequentialScan);

    public void FlushToDisk(Stream stream)
    {
        if (stream is FileStream file)
            file.Flush(flushToDisk: true);
        else
            stream.Flush();
    }

    /// <summary>Atomic on one volume: <see cref="File.Replace"/> keeps the destination's attributes and ACLs.</summary>
    public void Replace(string temporaryPath, string destinationPath)
    {
        try
        {
            File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // Some file systems do not implement ReplaceFile; a same-volume Move is the same guarantee.
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    public bool Exists(string path) => File.Exists(path);
}

/// <summary>
/// Replaces the wrapped-key slots of an existing container without re-encrypting its payload. The new
/// container is written to a sibling temp file, fully test-read with the key and the new secret, and only
/// then atomically swapped in. A failure at any step leaves the original file exactly as it was.
/// </summary>
public static class LockHeaderRewriter
{
    /// <summary>
    /// <paramref name="dataKey"/> must be the key of <paramref name="path"/>; <paramref name="slots"/> are
    /// the complete three slots for the new header. <paramref name="verifySecret"/> is the secret the caller
    /// expects to open the rewritten file with (a password or a Rescuer key).
    /// </summary>
    public static void Rewrite(
        string path,
        byte[] dataKey,
        LockSlot[] slots,
        string verifySecret,
        ILockFileOps? fileOps = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifySecret);
        var ops = fileOps ?? RealLockFileOps.Instance;
        var temp = LockPaths.TempPath(path) + ".hdr";

        LockHeader original;
        LockHeader header;
        using (var source = ops.OpenRead(path))
        {
            original = LockContainer.ReadHeader(source);
            if (!LockContainer.HeaderMatchesKey(original, dataKey))
                throw new LockDamagedException("header key");
            header = LockContainer.WithSlots(original, dataKey, slots);
        }

        try
        {
            ops.Delete(temp);

            // 1. Write the new header plus an untouched copy of the payload.
            using (var source = ops.OpenRead(path))
            using (var destination = ops.CreateNew(temp))
            {
                LockContainer.ReadHeader(source); // position after the old header
                destination.Write(header.Serialize());
                CopyPayload(source, destination, cancellationToken);
                ops.FlushToDisk(destination);
            }

            // 2. Prove the temp file is complete: header MAC, every frame and the new secret.
            using (var verify = ops.OpenRead(temp))
            {
                var tempHeader = LockContainer.ReadHeader(verify);
                if (!LockContainer.HeaderMatchesKey(tempHeader, dataKey))
                    throw new LockDamagedException("rewritten header");
                LockContainer.DecryptWithDataKey(verify, null, tempHeader, dataKey, cancellationToken: cancellationToken);
                verify.Position = 0;
                if (!LockContainer.TryUnwrap(LockContainer.ReadHeader(verify), verifySecret, out var check, out _))
                    throw new LockDamagedException("rewritten slot");
                LockSecrets.Wipe(check);
            }

            // 3. Swap atomically, then prove the installed file opens with the new secret.
            ops.Replace(temp, path);
            try
            {
                using var installed = ops.OpenRead(path);
                if (!LockContainer.TryUnwrap(LockContainer.ReadHeader(installed), verifySecret, out var check, out _))
                    throw new LockDamagedException("installed header");
                LockSecrets.Wipe(check);
            }
            catch
            {
                // Put the previous header back; the payload never changed, so the old secret works again.
                TryRestoreHeader(path, original, dataKey, ops, cancellationToken);
                throw;
            }
        }
        finally
        {
            try
            {
                ops.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover .hdr temp is harmless; the next rewrite replaces it.
            }
        }
    }

    private static void TryRestoreHeader(
        string path, LockHeader original, byte[] dataKey, ILockFileOps ops, CancellationToken cancellationToken)
    {
        var rollback = LockPaths.TempPath(path) + ".rollback";
        try
        {
            ops.Delete(rollback);
            using (var source = ops.OpenRead(path))
            using (var destination = ops.CreateNew(rollback))
            {
                LockContainer.ReadHeader(source);
                destination.Write(original.Serialize());
                CopyPayload(source, destination, cancellationToken);
                ops.FlushToDisk(destination);
            }
            ops.Replace(rollback, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException)
        {
            // Nothing else can be done without risking the payload; the caller still reports the failure.
        }
        finally
        {
            try
            {
                ops.Delete(rollback);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void CopyPayload(Stream source, Stream destination, CancellationToken cancellationToken)
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
                destination.Write(buffer, 0, read);
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }
}
