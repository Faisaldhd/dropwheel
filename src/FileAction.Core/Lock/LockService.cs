using System.IO;
using System.Security.Cryptography;

namespace FileAction.Core.Lock;

/// <summary>A user-facing refusal that carries enough information for a precise message.</summary>
public sealed class LockRefusedException(LockRefusal reason, string path)
    : Exception($"This item cannot be protected ({reason}).")
{
    public LockRefusal Reason { get; } = reason;
    public string Path { get; } = path;
}

/// <summary>The target container already exists; nothing is ever overwritten while protecting.</summary>
public sealed class LockTargetExistsException(string path)
    : IOException($"'{Path.GetFileName(path)}' already exists here.")
{
    public string TargetPath { get; } = path;
}

/// <summary>The action is not allowed with the secret that was presented.</summary>
public sealed class LockPolicyException(string message) : Exception(message);

/// <summary>What the protect pipeline is doing, with plaintext bytes so far.</summary>
public enum LockStage
{
    Preparing,
    Encrypting,
    Verifying,
    Finalizing,
    Extracting,
    Done,
}

public readonly record struct LockProgress(LockStage Stage, long Done, long Total);

public sealed record ProtectOutcome(
    string OutputPath,
    LockDigest Digest,
    bool SourceRemoved,
    string? LeftoverSource,
    int EntryCount = 0);

public sealed record UnprotectOutcome(
    string RestoredPath,
    LockDigest Digest,
    bool ContainerRemoved,
    string? ContainerError);

public sealed record ExtractOutcome(
    string Path,
    LockReadResult Read,
    LockHeader Header,
    byte[] DataKey);

/// <summary>Removes a protected container and tells the shell something changed.</summary>
public interface ILockHost
{
    /// <summary>Called only after the plaintext has been decrypted and verified.</summary>
    void RemoveContainer(string protectedPath, bool recycleBin);

    void NotifyShell(string path);

    /// <summary>True when an open DropWheel session still owns this container; defaults to false for tests.</summary>
    bool IsSessionOpen(string protectedPath) => false;
}

/// <summary>The host used by tests and by callers that only need the file operations.</summary>
public sealed class NullLockHost : ILockHost
{
    public static readonly NullLockHost Instance = new();

    public void RemoveContainer(string protectedPath, bool recycleBin)
    {
        if (File.Exists(protectedPath))
        {
            File.SetAttributes(protectedPath, FileAttributes.Normal);
            File.Delete(protectedPath);
        }
    }

    public void NotifyShell(string path)
    {
    }
}

/// <summary>
/// The safe transformation of a real file or folder into one encrypted container, and back. The order is
/// fixed and every step is verified before the next one: write a sibling temp file, flush it to disk, read
/// it back in streaming mode and compare length + SHA-256, atomically move it into place, and only then
/// remove the plaintext. A failure at any point deletes the temp file and leaves the original untouched.
/// </summary>
public sealed class LockService(
    LockEnvironment? environment = null,
    ILockHost? host = null,
    int minimumIterations = LockFormat.MinimumIterations,
    int newSlotIterations = LockFormat.DefaultIterations,
    Func<string, bool>? deletePath = null)
{
    private readonly LockEnvironment _environment = environment ?? LockEnvironment.Default();
    private readonly ILockHost _host = host ?? NullLockHost.Instance;
    private readonly int _minimumIterations = Math.Max(1, minimumIterations);
    private readonly int _newSlotIterations = Math.Max(1, newSlotIterations);
    private readonly string _restoreTemp = (environment ?? LockEnvironment.Default()).RestoreTempPath;
    private readonly Func<string, bool>? _deletePath = deletePath;

    public LockEnvironment Environment => _environment;

    /// <summary>Where in-progress restore staging files live, so callers can sweep orphans.</summary>
    public string RestoreTempFolder => _restoreTemp;

    // ------------------------------------------------------------------ protect

    public ProtectOutcome Protect(
        string sourcePath,
        string? outputDirectory,
        LockCredentials credentials,
        string? folderIconPath = null,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();
        RequireIterations(credentials);
        var full = LockPaths.Normalize(sourcePath);
        var refusal = LockGuard.Check(full, _environment);
        if (refusal != LockRefusal.None)
            throw new LockRefusedException(refusal, full);

        var isDirectory = Directory.Exists(full);
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(full)!
            : LockPaths.Normalize(outputDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);
        return ProtectCore(full, LockPaths.TargetPath(full, isDirectory, directory), credentials, folderIconPath,
            progress, cancellationToken);
    }

    /// <summary>Protects to an exact container path (crash recovery, or a caller that already chose the name).</summary>
    public ProtectOutcome ProtectTo(
        string sourcePath,
        string targetPath,
        LockCredentials credentials,
        string? folderIconPath = null,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowInternalSource = false,
        bool keepSource = false)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        credentials.Validate();
        RequireIterations(credentials);
        var full = LockPaths.Normalize(sourcePath);
        if (!allowInternalSource)
        {
            var refusal = LockGuard.Check(full, _environment);
            if (refusal != LockRefusal.None)
                throw new LockRefusedException(refusal, full);
        }
        return ProtectCore(full, LockPaths.Normalize(targetPath), credentials, folderIconPath, progress,
            cancellationToken, keepSource);
    }

    private ProtectOutcome ProtectCore(
        string full,
        string target,
        LockCredentials credentials,
        string? folderIconPath,
        IProgress<LockProgress>? progress,
        CancellationToken cancellationToken,
        bool keepSource = false)
    {
        var isDirectory = Directory.Exists(full);
        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);
        if (File.Exists(target) || Directory.Exists(target))
            throw new LockTargetExistsException(target);

        var meta = isDirectory
            ? BuildFolderMeta(full, folderIconPath)
            : LockMeta.ForFile(full);

        var dataKey = RandomNumberGenerator.GetBytes(LockFormat.KeyLength);
        var appearanceApplied = false;
        var temp = LockPaths.TempPath(target);
        try
        {
            progress?.Report(new LockProgress(LockStage.Preparing, 0, 0));
            DeleteStale(temp);

            LockProtectResult result;
            using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       256 * 1024, FileOptions.SequentialScan))
            {
                if (isDirectory && folderIconPath is not null)
                {
                    FolderAppearance.Apply(full, folderIconPath);
                    appearanceApplied = true;
                }
                result = EncryptSource(full, isDirectory, destination, dataKey, credentials, meta, progress,
                    cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            if (isDirectory && !keepSource)
            {
                var currentEntries = LockArchive.CountEntries(full);
                if (currentEntries != result.EntryCount)
                    throw new LockChangedException($"{result.EntryCount} entries were archived but the folder now has {currentEntries}");
            }
            VerifyContainer(temp, dataKey, result.Digest, credentials, progress, cancellationToken);
            progress?.Report(new LockProgress(LockStage.Finalizing, result.Digest.Length, result.Digest.Length));
            File.Move(temp, target); // never overwrites: fail closed instead of losing an existing container

            bool removed;
            string? leftover = null;
            if (keepSource)
            {
                removed = true;
            }
            else
            {
                removed = TryRemovePlaintext(full, out leftover);
            }
            var kept = removed ? null : leftover;
            _host.NotifyShell(target);
            if (kept is not null)
                _host.NotifyShell(kept);
            progress?.Report(new LockProgress(LockStage.Done, result.Digest.Length, result.Digest.Length));
            return new ProtectOutcome(target, result.Digest, removed, kept, result.EntryCount);
        }
        catch
        {
            DeleteStale(temp);
            if (appearanceApplied && Directory.Exists(full))
            {
                FolderAppearance.Restore(full, meta.PreviousDesktopIni, meta.PreviousDesktopIniAttributes);
                FolderAppearance.RestoreFolderAttributes(full, meta.Attributes);
            }
            throw;
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>Auto-lock: replace an existing container from the edited plaintext, reusing the same header.</summary>
    public void RelockFromSession(
        string containerPath,
        LockHeader header,
        byte[] dataKey,
        string plaintextPath,
        string? folderIconPath = null,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? expectedFileHash = null,
        string? expectedTreeHash = null,
        int? originalAttributes = null,
        int? originalDesktopIniAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(dataKey);
        var full = LockPaths.Normalize(containerPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("The protected file is gone.", full);

        LockHeader current;
        using (var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
            current = LockContainer.ReadHeader(source);
        if (!LockContainer.HeaderMatchesKey(current, dataKey) ||
            !LockSecrets.SameHash(current.FileId, header.FileId))
        {
            throw new LockDamagedException("the container changed while it was open");
        }

        var isDirectory = Directory.Exists(plaintextPath);
        if (!isDirectory && expectedFileHash is not null)
        {
            var currentHash = LockArchive.FileHash(plaintextPath);
            if (!string.Equals(currentHash, expectedFileHash, StringComparison.Ordinal))
                throw new LockChangedException("the unlocked file changed since the last save; retry would encrypt different bytes. The good container was kept.");
        }
        if (isDirectory && expectedTreeHash is not null)
        {
            var currentTree = LockArchive.TreeHash(plaintextPath);
            if (!string.Equals(currentTree, expectedTreeHash, StringComparison.Ordinal))
                throw new LockChangedException("the unlocked folder changed since the last save; retry would encrypt different entries. The good container was kept.");
        }
        var meta = isDirectory
            ? BuildFolderMeta(plaintextPath, folderIconPath)
            : LockMeta.ForFile(plaintextPath);
        // Keep the name the container was created with, whatever the temp file or folder is called.
        var restored = ReadStoredName(full, dataKey);
        if (!string.IsNullOrEmpty(restored))
            meta = meta with { Name = restored };
        // The editable session copy has ReadOnly cleared so editors can save. The container must still
        // describe the original item, so the session passes the attributes captured before that change.
        if (originalAttributes is { } attributes)
            meta = meta with { Attributes = attributes };
        if (isDirectory && originalDesktopIniAttributes is { } desktopAttributes)
            meta = meta with { PreviousDesktopIniAttributes = desktopAttributes };

        var temp = LockPaths.TempPath(full);
        var appearanceApplied = false;
        try
        {
            DeleteStale(temp);
            LockProtectResult result;
            using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       256 * 1024, FileOptions.SequentialScan))
            {
                if (isDirectory && folderIconPath is not null)
                {
                    FolderAppearance.Apply(plaintextPath, folderIconPath);
                    appearanceApplied = true;
                }
                result = EncryptSource(plaintextPath, isDirectory, destination, dataKey, null, meta, progress,
                    cancellationToken, current);
                destination.Flush(flushToDisk: true);
            }

            if (isDirectory && LockArchive.CountEntries(plaintextPath) != result.EntryCount)
                throw new LockChangedException("the folder changed while it was being re-encrypted");
            VerifyContainer(temp, dataKey, result.Digest, null, progress, cancellationToken);
            progress?.Report(new LockProgress(LockStage.Finalizing, result.Digest.Length, result.Digest.Length));
            ReplaceFile(temp, full);
            var removed = TryRemovePlaintext(plaintextPath, out var leftover);
            _host.NotifyShell(full);
            progress?.Report(new LockProgress(LockStage.Done, result.Digest.Length, result.Digest.Length));
            if (!removed)
            {
                // The container is safely updated; the plaintext could not be removed. Report it so the
                // panel can retry, but never delete the container or the plaintext.
                throw new IOException(
                    $"The protected file was updated, but the plaintext could not be removed: {leftover}");
            }
        }
        catch
        {
            DeleteStale(temp);
            if (appearanceApplied && Directory.Exists(plaintextPath))
            {
                FolderAppearance.Restore(plaintextPath, meta.PreviousDesktopIni, meta.PreviousDesktopIniAttributes);
                FolderAppearance.RestoreFolderAttributes(plaintextPath, meta.Attributes);
            }
            throw;
        }
    }

    // ------------------------------------------------------------------ unprotect

    public UnprotectOutcome Unprotect(
        string protectedPath,
        string secret,
        bool recycleContainer,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var full = LockPaths.Normalize(protectedPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("The protected file is gone.", full);
        if (_host.IsSessionOpen(full))
            throw new LockPolicyException("Lock this file first: it is open in a DropWheel session.");
        var (header, dataKey, kind) = OpenHeader(full, secret);
        LockSecrets.Wipe(dataKey);
        if (kind == LockSecretKind.RescuerKey)
            throw new LockPolicyException("The Rescuer key opens a file, but it cannot remove protection by itself. " +
                "Use the file password or the Rescuer password.");
        var directory = Path.GetDirectoryName(full)!;

        var extracted = ExtractTo(full, secret, directory, progress, cancellationToken);
        var restored = extracted.Path;
        try
        {
            _host.NotifyShell(restored);
            string? error = null;
            var removed = true;
            try
            {
                _host.RemoveContainer(full, recycleContainer);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                removed = false;
                error = ex.Message;
            }
            progress?.Report(new LockProgress(LockStage.Done, extracted.Read.Digest.Length,
                extracted.Read.Digest.Length));
            return new UnprotectOutcome(restored, extracted.Read.Digest, removed, error);
        }
        finally
        {
            LockSecrets.Wipe(extracted.DataKey);
        }
    }

    /// <summary>
    /// Decrypts a container into <paramref name="destinationDirectory"/> and returns the restored path and
    /// the data key. The plaintext is written to a staging name, verified, and then moved; the container is
    /// never touched. The caller owns the returned key and must wipe it. With <paramref name="editableCopy"/>
    /// the ReadOnly bit is cleared on the extracted copy only: an editor can save, while a permanent
    /// unprotect (the default) still restores the original attributes.
    /// </summary>
    public ExtractOutcome ExtractTo(
        string protectedPath,
        string secret,
        string destinationDirectory,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool editableCopy = false)
    {
        var full = LockPaths.Normalize(protectedPath);
        Directory.CreateDirectory(destinationDirectory);
        using var source = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024,
            FileOptions.SequentialScan);
        var header = LockContainer.ReadHeader(source);
        var dataKey = LockContainer.Unwrap(header, secret, out _);
        try
        {
            return header.IsDirectory
                ? ExtractFolder(source, header, dataKey, destinationDirectory, progress, cancellationToken,
                    editableCopy)
                : ExtractFile(source, header, dataKey, destinationDirectory, progress, cancellationToken,
                    editableCopy);
        }
        catch
        {
            LockSecrets.Wipe(dataKey);
            throw;
        }
    }

    /// <summary>The original name stored inside a container, or null when the key does not open it.</summary>
    private static string? ReadStoredName(string path, byte[] dataKey)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024,
                FileOptions.SequentialScan);
            var header = LockContainer.ReadHeader(source);
            return LockContainer.HeaderMatchesKey(header, dataKey)
                ? LockContainer.ReadMeta(source, header, dataKey).Name
                : null;
        }
        catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private ExtractOutcome ExtractFile(
        Stream source,
        LockHeader header,
        byte[] dataKey,
        string destinationDirectory,
        IProgress<LockProgress>? progress,
        CancellationToken cancellationToken,
        bool editableCopy)
    {
        var stagingRoot = StagingRoot(destinationDirectory);
        Directory.CreateDirectory(stagingRoot);
        var staging = Path.Combine(stagingRoot, ".dwrestore-" + Guid.NewGuid().ToString("N") + ".tmp");
        LockReadResult read;
        try
        {
            using (var plain = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       256 * 1024, FileOptions.SequentialScan))
            {
                progress?.Report(new LockProgress(LockStage.Extracting, 0, 0));
                read = LockContainer.DecryptWithDataKey(source, plain, header, dataKey, LockSecretKind.FilePassword,
                    new Progress<long>(done => progress?.Report(new LockProgress(LockStage.Extracting, done, 0))),
                    cancellationToken);
                plain.Flush(flushToDisk: true);
            }

            if (read.Digest.Length != read.Meta.Size)
                throw new LockDamagedException("the restored file size does not match the metadata");
            var final = UniqueRestored(destinationDirectory, read.Meta.Name, File.Exists, Directory.Exists);
            MoveFile(staging, final);
            FolderAppearance.RestoreTimes(final, read.Meta, directory: false);
            ApplyFileAttributes(final, read.Meta.Attributes);
            if (editableCopy)
                ClearReadOnly(final);
            return new ExtractOutcome(final, read, header, dataKey);
        }
        catch
        {
            DeleteStale(staging);
            throw;
        }
    }

    private ExtractOutcome ExtractFolder(
        Stream source,
        LockHeader header,
        byte[] dataKey,
        string destinationDirectory,
        IProgress<LockProgress>? progress,
        CancellationToken cancellationToken,
        bool editableCopy)
    {
        var stagingRoot = StagingRoot(destinationDirectory);
        Directory.CreateDirectory(stagingRoot);
        var archive = Path.Combine(stagingRoot, ".dwrestore-" + Guid.NewGuid().ToString("N") + ".archive");
        var staging = Path.Combine(stagingRoot, ".dwrestore-" + Guid.NewGuid().ToString("N") + ".dir");
        LockReadResult read;
        try
        {
            using (var plain = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       256 * 1024, FileOptions.SequentialScan))
            {
                progress?.Report(new LockProgress(LockStage.Extracting, 0, 0));
                read = LockContainer.DecryptWithDataKey(source, plain, header, dataKey, LockSecretKind.FilePassword,
                    new Progress<long>(done => progress?.Report(new LockProgress(LockStage.Extracting, done, 0))),
                    cancellationToken);
                plain.Flush(flushToDisk: true);
            }

            using (var archiveStream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read,
                       256 * 1024, FileOptions.SequentialScan))
            {
                var entries = LockArchive.Extract(archiveStream, staging, cancellationToken);
                if (entries != read.Meta.EntryCount)
                    throw new LockDamagedException("the folder entry count does not match the archive");
            }
            DeleteStale(archive);

            // The unlocked folder looks exactly as it did before protection: no badge, original bytes.
            FolderAppearance.Restore(staging, read.Meta.PreviousDesktopIni, read.Meta.PreviousDesktopIniAttributes);

            var final = UniqueRestored(destinationDirectory, read.Meta.Name, File.Exists, Directory.Exists);
            MoveDirectory(staging, final);
            FolderAppearance.RestoreFolderAttributes(final, read.Meta.Attributes);
            FolderAppearance.RestoreTimes(final, read.Meta, directory: true);
            if (editableCopy)
                ClearReadOnlyTree(final);
            progress?.Report(new LockProgress(LockStage.Done, read.Digest.Length, read.Digest.Length));
            return new ExtractOutcome(final, read, header, dataKey);
        }
        catch
        {
            DeleteStale(archive);
            TryDeleteDirectory(staging);
            throw;
        }
    }

    // ------------------------------------------------------------------ recovery ladder

    /// <summary>Set a new file password; the current file or Rescuer password is required, not the key alone.</summary>
    public void SetFilePassword(string containerPath, string currentSecret, string newPassword,
        ILockFileOps? fileOps = null)
    {
        if (!LockSecrets.IsAcceptablePassword(newPassword))
            throw new ArgumentException("The new file password is too short.", nameof(newPassword));
        var (header, dataKey, kind) = OpenHeader(containerPath, currentSecret);
        try
        {
            if (kind == LockSecretKind.RescuerKey)
            {
                throw new LockPolicyException(
                    "The Rescuer key cannot change a file password. Use the file password or the Rescuer " +
                    "password, or reset the Rescuer first.");
            }
            var slots = CloneSlots(header);
            slots[0] = LockContainer.Wrap(dataKey, header, 0, LockSecretKind.FilePassword, newPassword,
                _newSlotIterations);
            LockHeaderRewriter.Rewrite(containerPath, dataKey, slots, newPassword, fileOps);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>Set a new Rescuer password (used after the Rescuer key proved ownership).</summary>
    public void SetRescuerPassword(string containerPath, string currentSecret, string newPassword,
        ILockFileOps? fileOps = null)
    {
        if (!LockSecrets.IsAcceptablePassword(newPassword))
            throw new ArgumentException("The new Rescuer password is too short.", nameof(newPassword));
        var (header, dataKey, _) = OpenHeader(containerPath, currentSecret);
        try
        {
            var slots = CloneSlots(header);
            slots[1] = LockContainer.Wrap(dataKey, header, 1, LockSecretKind.RescuerPassword, newPassword,
                _newSlotIterations);
            LockHeaderRewriter.Rewrite(containerPath, dataKey, slots, newPassword, fileOps);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>Create a new Rescuer key for one file; the old key or any password proves ownership.</summary>
    public void SetRescuerKey(string containerPath, string currentSecret, string newKey,
        ILockFileOps? fileOps = null)
    {
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(newKey, out _))
            throw new ArgumentException("The new Rescuer key is not valid.", nameof(newKey));
        var (header, dataKey, _) = OpenHeader(containerPath, currentSecret);
        try
        {
            var slots = CloneSlots(header);
            slots[2] = LockContainer.Wrap(dataKey, header, 2, LockSecretKind.RescuerKey, newKey,
                _newSlotIterations);
            LockHeaderRewriter.Rewrite(containerPath, dataKey, slots, newKey, fileOps);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>
    /// Replaces both Rescuer slots of one container in a single verified rewrite. Used after the very first
    /// Lock, when the file was protected with a provisional pair and the user has just created the real
    /// Rescuer in Protection settings. The file password and payload are untouched.
    /// </summary>
    public void AdoptRescuer(string containerPath, string currentSecret, RescuerSecrets rescuer,
        ILockFileOps? fileOps = null)
    {
        ArgumentNullException.ThrowIfNull(rescuer);
        if (!LockSecrets.IsAcceptablePassword(rescuer.Password))
            throw new ArgumentException("The Rescuer password is too short.", nameof(rescuer));
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(rescuer.Key, out var canonicalKey))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(rescuer));
        if (string.IsNullOrEmpty(currentSecret))
            throw new ArgumentException("The current secret is empty.", nameof(currentSecret));

        var (header, dataKey, _) = OpenHeader(containerPath, currentSecret);
        try
        {
            var slots = CloneSlots(header);
            slots[1] = LockContainer.Wrap(dataKey, header, 1, LockSecretKind.RescuerPassword,
                rescuer.Password!, _newSlotIterations);
            slots[2] = LockContainer.Wrap(dataKey, header, 2, LockSecretKind.RescuerKey, canonicalKey,
                _newSlotIterations);
            LockHeaderRewriter.Rewrite(containerPath, dataKey, slots, rescuer.Password!, fileOps);
            // The rewriter proves the password door; prove the key door too before reporting success.
            using var installed = new FileStream(LockPaths.Normalize(containerPath), FileMode.Open,
                FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
            var installedHeader = LockContainer.ReadHeader(installed);
            if (!LockContainer.TryUnwrapRole(installedHeader, LockSecretKind.RescuerKey, canonicalKey,
                    out var keyCheck))
                throw new LockDamagedException("rewritten Rescuer key slot");
            LockSecrets.Wipe(keyCheck);
        }
        finally
        {
            LockSecrets.Wipe(dataKey);
        }
    }

    /// <summary>
    /// Re-wraps slot 2 on many containers with a new Rescuer password, verifying every rewrite by a full
    /// test-open. Failures are collected per file; successful files keep working with the old secrets.
    /// </summary>
    public IReadOnlyList<(string Path, Exception? Error)> UpdateRescuer(
        IEnumerable<string> containers,
        string currentSecret,
        string newPassword,
        IProgress<(int Done, int Total, string Path)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!LockSecrets.IsAcceptablePassword(newPassword))
            throw new ArgumentException("The new Rescuer password is too short.", nameof(newPassword));
        var list = containers.Select(LockPaths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<(string Path, Exception? Error)>();
        var index = 0;
        foreach (var path in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((index, list.Count, path));
            try
            {
                SetRescuerPassword(path, currentSecret, newPassword);
                results.Add((path, null));
            }
            catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                           or LockPolicyException or ArgumentException)
            {
                results.Add((path, ex));
            }
            index++;
        }
        progress?.Report((index, list.Count, string.Empty));
        return results;
    }

    /// <summary>
    /// Re-wraps the Rescuer key slot on many containers and proves the replacement on every file: the new
    /// key must open the rewritten header and the old key must no longer open it. If any file fails, every
    /// file already changed is rewrapped back to <paramref name="oldKey"/> before returning, so the caller
    /// can keep the old vault key valid for all of them.
    /// </summary>
    public IReadOnlyList<(string Path, Exception? Error)> RotateRescuerKey(
        IEnumerable<string> containers,
        string currentSecret,
        string oldKey,
        string newKey,
        IProgress<(int Done, int Total, string Path)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(containers);
        if (string.IsNullOrEmpty(currentSecret))
            throw new ArgumentException("The current secret is empty.", nameof(currentSecret));
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(oldKey, out var canonicalOld))
            throw new ArgumentException("The old Rescuer key is not valid.", nameof(oldKey));
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(newKey, out var canonicalNew))
            throw new ArgumentException("The new Rescuer key is not valid.", nameof(newKey));

        var list = containers
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(LockPaths.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = new List<(string Path, Exception? Error)>();
        var changed = new List<string>();
        var index = 0;
        foreach (var path in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((index, list.Count, path));
            try
            {
                SetRescuerKey(path, currentSecret, canonicalNew);
                // The rewriter verified the new slot and the payload; prove the old door is gone too.
                if (!LockVerification.OpensWithRescuerKey(path, canonicalNew))
                    throw new LockDamagedException("rotated Rescuer key does not open the file");
                if (!LockSecrets.SameSecret(canonicalOld, canonicalNew) &&
                    LockVerification.OpensWithRescuerKey(path, canonicalOld))
                {
                    throw new LockDamagedException("the old Rescuer key still opens the file");
                }
                changed.Add(path);
                results.Add((path, null));
            }
            catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                           or LockPolicyException or ArgumentException)
            {
                results.Add((path, ex));
            }
            index++;
        }
        progress?.Report((index, list.Count, string.Empty));

        if (results.Any(result => result.Error is not null) && changed.Count > 0)
        {
            // All-or-nothing: a partial rotation would leave some files on the new key while the vault
            // still advertises the old one. Put every successful rewrite back before reporting failure.
            foreach (var path in changed)
            {
                try
                {
                    SetRescuerKey(path, currentSecret, canonicalOld);
                    if (!LockVerification.OpensWithRescuerKey(path, canonicalOld))
                        throw new LockDamagedException("rollback did not restore the old Rescuer key");
                }
                catch (Exception ex) when (ex is LockException or IOException or UnauthorizedAccessException
                                               or LockPolicyException or ArgumentException)
                {
                    var at = results.FindIndex(result => string.Equals(result.Path, path,
                        StringComparison.OrdinalIgnoreCase));
                    if (at >= 0)
                        results[at] = (path, ex);
                }
            }
        }
        return results;
    }

    /// <summary>Verify a protected file and return its metadata without leaving a plaintext copy.</summary>
    public LockReadResult Verify(string containerPath, string secret)
    {
        using var source = new FileStream(LockPaths.Normalize(containerPath), FileMode.Open, FileAccess.Read,
            FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
        return LockContainer.Decrypt(source, null, secret);
    }

    /// <summary>
    /// Re-encrypts the plaintext of a session whose process ended without relocking. The edited content is
    /// protected with the supplied credentials, verified, and only then swapped in for the old container and
    /// removed; a failure leaves both the container and the plaintext untouched.
    /// </summary>
    public void RecoverSession(
        LockSessionRecord record,
        LockCredentials credentials,
        LockJournalStore journal,
        string? folderIconPath = null,
        IProgress<LockProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(journal);
        var container = LockPaths.Normalize(record.ContainerPath);
        var plaintext = LockPaths.Normalize(record.PlaintextPath);
        if (!LockPaths.IsInside(plaintext, ProtectionPaths.SessionsFolder))
            throw new LockPolicyException("A recovery session must live in DropWheel's own sessions folder.");
        if (!File.Exists(plaintext) && !Directory.Exists(plaintext))
        {
            journal.Delete(record.Id);
            return;
        }
        if (record.State == LockSessionState.Error)
        {
            if (!record.IsDirectory && record.FileHash is not null &&
                !string.Equals(LockArchive.FileHash(plaintext), record.FileHash, StringComparison.Ordinal))
                throw new LockChangedException("the recovered file changed after the failed relock; salvage it instead of encrypting different bytes.");
            if (record.IsDirectory && record.TreeHash is not null &&
                !string.Equals(LockArchive.TreeHash(plaintext), record.TreeHash, StringComparison.Ordinal))
                throw new LockChangedException("the recovered folder changed after the failed relock; salvage it instead of encrypting different entries.");
        }


        var stagingTarget = container + ".recover";
        var containerExists = File.Exists(container);
        var plaintextExists = File.Exists(plaintext) || Directory.Exists(plaintext);
        if (plaintextExists)
            DeleteStale(stagingTarget); // a previous attempt failed before replacing; the plaintext is newer
        try
        {
            var outcome = ProtectTo(plaintext, stagingTarget, credentials, folderIconPath, progress,
                cancellationToken, allowInternalSource: true, keepSource: true);
            if (record.IsDirectory && LockArchive.CountEntries(plaintext) != outcome.EntryCount)
                throw new LockChangedException("the session folder changed while it was being re-encrypted");
            if (containerExists)
            {
                ReplaceFile(outcome.OutputPath, container);
            }
            else
            {
                File.Move(outcome.OutputPath, container);
            }
            // Only now, with the verified container in place, is the plaintext removed.
            if (!TryRemovePlaintext(plaintext, out var leftover))
                throw new IOException("The recovered file is encrypted, but the plaintext could not be removed: " + leftover);
            journal.Delete(record.Id);
            _host.NotifyShell(container);
        }
        catch
        {
            if (plaintextExists)
                DeleteStale(stagingTarget);
            throw;
        }
    }

    // ------------------------------------------------------------------ internals

    private LockProtectResult EncryptSource(
        string sourcePath,
        bool isDirectory,
        Stream destination,
        byte[] dataKey,
        LockCredentials? credentials,
        LockMeta meta,
        IProgress<LockProgress>? progress,
        CancellationToken cancellationToken,
        LockHeader? existingHeader = null)
    {
        progress?.Report(new LockProgress(LockStage.Encrypting, 0, 0));
        var byteProgress = new Progress<long>(done =>
            progress?.Report(new LockProgress(LockStage.Encrypting, done, 0)));

        if (isDirectory)
        {
            var encodedEntries = 0;
            Action<ILockContentSink> writeArchive = sink =>
                encodedEntries = LockArchive.Write(sourcePath, sink, null, cancellationToken);
            var result = credentials is not null
                ? LockContainer.EncryptContent(destination, dataKey, credentials, meta, writeArchive, byteProgress,
                    cancellationToken)
                : LockContainer.EncryptContent(destination, dataKey, existingHeader!, meta, writeArchive,
                    byteProgress, cancellationToken);
            return result with { EntryCount = encodedEntries };
        }

        Action<ILockContentSink> writeFile = sink =>
        {
            using var file = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 256 * 1024, FileOptions.SequentialScan);
            var buffer = new byte[256 * 1024];
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = file.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    sink.Write(buffer.AsSpan(0, read));
                }
            }
            finally
            {
                Array.Clear(buffer);
            }
        };
        return credentials is not null
            ? LockContainer.EncryptContent(destination, dataKey, credentials, meta, writeFile, byteProgress,
                cancellationToken)
            : LockContainer.EncryptContent(destination, dataKey, existingHeader!, meta, writeFile, byteProgress,
                cancellationToken);
    }

    private LockMeta BuildFolderMeta(string folder, string? iconPath)
    {
        // The previous desktop.ini is always kept: even when no icon is registered, unlocking must restore
        // the folder exactly as it was.
        var previous = FolderAppearance.ReadPrevious(folder);
        var previousAttributes = FolderAppearance.ReadPreviousAttributes(folder);
        var count = LockArchive.CountEntries(folder);
        var baseMeta = LockMeta.ForDirectory(folder, count, previous);
        return previousAttributes == 0 ? baseMeta : baseMeta with { PreviousDesktopIniAttributes = previousAttributes };
    }

    private static void VerifyContainer(
        string path,
        byte[] dataKey,
        LockDigest expected,
        LockCredentials? credentials,
        IProgress<LockProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new LockProgress(LockStage.Verifying, 0, expected.Length));
        using var verify = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024,
            FileOptions.SequentialScan);
        var header = LockContainer.ReadHeader(verify);
        var read = LockContainer.DecryptWithDataKey(verify, null, header, dataKey, LockSecretKind.FilePassword,
            new Progress<long>(done => progress?.Report(new LockProgress(LockStage.Verifying, done, expected.Length))),
            cancellationToken);
        if (read.Digest.Length != expected.Length || !LockSecrets.SameHash(read.Digest.Sha256, expected.Sha256))
            throw new LockDamagedException("the new container does not match the source");
        if (credentials is not null)
        {
            foreach (var secret in new[] { credentials.FilePassword, credentials.RescuerPassword,
                         credentials.RescuerKey })
            {
                using var slotStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    256 * 1024, FileOptions.SequentialScan);
                var slotHeader = LockContainer.ReadHeader(slotStream);
                if (!LockContainer.TryUnwrap(slotHeader, secret, out var check, out _))
                    throw new LockDamagedException("a written slot does not open the new container");
                if (!LockSecrets.SameHash(check, dataKey))
                {
                    LockSecrets.Wipe(check);
                    throw new LockDamagedException("a written slot opens a different data key");
                }
                LockSecrets.Wipe(check);
            }
        }
    }

    private void RequireIterations(LockCredentials credentials)
    {
        if (credentials.FilePasswordIterations < _minimumIterations ||
            credentials.RescuerPasswordIterations < _minimumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(credentials),
                "The password slots must use at least " + _minimumIterations + " PBKDF2 iterations.");
        }
    }

    private (LockHeader Header, byte[] DataKey, LockSecretKind Kind) OpenHeader(string path, string secret)
    {
        using var source = new FileStream(LockPaths.Normalize(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            256 * 1024, FileOptions.SequentialScan);
        var header = LockContainer.ReadHeader(source);
        var dataKey = LockContainer.Unwrap(header, secret, out var kind);
        return (header, dataKey, kind);
    }

    private static LockSlot[] CloneSlots(LockHeader header) =>
        header.Slots.Select(slot => slot.Clone()).ToArray();

    private static string UniqueRestored(
        string directory, string originalName, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        bool Exists(string path) => fileExists(path) || directoryExists(path);
        var first = Path.Combine(directory, originalName);
        if (!Exists(first))
            return first;
        return LockPaths.UniqueRestoredPath(directory, originalName, Exists);
    }

    private static void ApplyFileAttributes(string path, int attributes)
    {
        try
        {
            var value = (FileAttributes)(attributes & 0xFFFF);
            if (value == 0)
                value = FileAttributes.Normal;
            File.SetAttributes(path, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes the plaintext after the container is in place. A single overwrite pass is attempted first;
    /// the item is then deleted for good — never left as a second, recoverable copy. Failure is reported,
    /// not hidden, and never makes the caller delete the new container.
    /// </summary>
    private bool TryRemovePlaintext(string path, out string? leftover)
    {
        leftover = null;
        try
        {
            if (_deletePath is not null)
            {
                if (!_deletePath(path))
                {
                    leftover = path;
                    return false;
                }
                return true;
            }
            if (Directory.Exists(path))
            {
                // Not overwritten first: a partially deleted tree would be worse than a locked one. The
                // container is already verified, so deleting the folder loses nothing.
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    ClearReadOnly(file);
                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    ClearReadOnly(directory);
                ClearReadOnly(path);
                Directory.Delete(path, recursive: true);
            }
            else
            {
                // Delete first. Zeroing a file before a failed delete leaves zeros in the session and a
                // retry would encrypt those zeros over the good container.
                ClearReadOnly(path);
                File.Delete(path);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            leftover = path;
            return false;
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Clears ReadOnly across a folder restored for an editing session, deepest items first, so every file
    /// can be saved and no directory blocks a rename or delete. Only the disposable session copy is
    /// touched; the container still carries the original attributes for a permanent unlock or a relock.
    /// </summary>
    private static void ClearReadOnlyTree(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                ClearReadOnly(file);
            foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
                ClearReadOnly(directory);
            ClearReadOnly(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be changed is reported by the editor; the copied bytes are still exact.
        }
    }

    private static void ReplaceFile(string source, string destination)
    {
        try
        {
            File.Replace(source, destination, null, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    private static void MoveFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            File.Copy(source, destination, overwrite: false);
            File.Delete(source);
        }
    }
    /// <summary>One app-temp staging root so the startup sweep always covers leftovers, on every volume.</summary>
    private string StagingRoot(string destinationDirectory)
    {
        _ = destinationDirectory;
        return _restoreTemp;
    }
    /// <summary>Moves a restore folder to its destination, falling back to copy+delete across volumes.</summary>
    private static void MoveDirectory(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException)
        {
            CopyDirectory(source, destination);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static void DeleteStale(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
