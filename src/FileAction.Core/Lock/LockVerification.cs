using System.IO;

namespace FileAction.Core.Lock;

/// <summary>
/// Proves that the Rescuer password and key typed for this session really open an existing container
/// before they are baked into new ones. The check reads only the header and unwraps slot 1 and slot 2, so
/// it never decrypts the payload; a typo is refused with a clear message instead of silently creating
/// files whose recovery door uses an unknown secret.
/// </summary>
public static class LockVerification
{
    /// <summary>
    /// True when both values open the Rescuer password slot and the Rescuer key slot of
    /// <paramref name="containerPath"/>. A file password is rejected even if it happens to match the
    /// typed text, because the kind of the slot that accepted it is part of the answer.
    /// </summary>
    public static bool OpensWithRescuer(string containerPath, string rescuerPassword, string rescuerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        if (string.IsNullOrEmpty(rescuerPassword) ||
            !global::FileAction.Core.Lock.RescuerKey.TryParse(rescuerKey, out var canonical))
        {
            return false;
        }

        using var source = new FileStream(LockPaths.Normalize(containerPath), FileMode.Open, FileAccess.Read,
            FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
        var header = LockContainer.ReadHeader(source);
        return Opens(header, rescuerPassword, canonical);
    }

    /// <summary>
    /// The first existing container whose two Rescuer slots open with the typed values, or null when the
    /// values do not open any of them. Unreadable or damaged candidates are skipped: the caller only needs
    /// one positive proof, and a stray file must not block the prompt.
    /// </summary>
    public static string? FindOpeningContainer(IEnumerable<string> containers, string rescuerPassword,
        string rescuerKey)
    {
        ArgumentNullException.ThrowIfNull(containers);
        if (string.IsNullOrEmpty(rescuerPassword) ||
            !global::FileAction.Core.Lock.RescuerKey.TryParse(rescuerKey, out var canonical))
        {
            return null;
        }

        foreach (var container in containers)
        {
            if (string.IsNullOrWhiteSpace(container) || !File.Exists(container))
                continue;
            try
            {
                using var source = new FileStream(LockPaths.Normalize(container), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
                var header = LockContainer.ReadHeader(source);
                if (Opens(header, rescuerPassword, canonical))
                    return container;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                           or ArgumentException)
            {
                // A container that cannot be read proves nothing; try the next candidate.
            }
        }
        return null;
    }

    /// <summary>
    /// True when one typed secret (the Rescuer password or the Rescuer key) opens its own Rescuer slot of
    /// <paramref name="containerPath"/>. A file password is rejected even if it happens to match: only the
    /// two Rescuer roles are tried. Used to re-authenticate before a password reset changes the vault.
    /// </summary>
    public static bool OpensWithRescuerSecret(string containerPath, string typed, out LockSecretKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        kind = LockSecretKind.FilePassword;
        if (string.IsNullOrEmpty(typed))
            return false;
        try
        {
            using var source = new FileStream(LockPaths.Normalize(containerPath), FileMode.Open, FileAccess.Read,
                FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
            return TryRescuerSecret(LockContainer.ReadHeader(source), typed, out kind);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                       or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first existing container that one typed Rescuer password or key opens. Unlike
    /// <see cref="FindOpeningContainer"/> this needs only one Rescuer door, which is exactly the proof
    /// required before the reset dialog may touch the vault.
    /// </summary>
    public static (string Path, LockSecretKind Kind)? FindOpeningContainerWithRescuerSecret(
        IEnumerable<string> containers, string typed)
    {
        ArgumentNullException.ThrowIfNull(containers);
        if (string.IsNullOrEmpty(typed))
            return null;
        foreach (var container in containers)
        {
            if (string.IsNullOrWhiteSpace(container) || !File.Exists(container))
                continue;
            try
            {
                using var source = new FileStream(LockPaths.Normalize(container), FileMode.Open, FileAccess.Read,
                    FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
                var header = LockContainer.ReadHeader(source);
                if (TryRescuerSecret(header, typed, out var kind))
                    return (container, kind);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                           or ArgumentException)
            {
                // A container that cannot be read proves nothing; try the next candidate.
            }
        }
        return null;
    }

    /// <summary>True when the current key slot (only) opens; used to prove a rotation replaced it.</summary>
    public static bool OpensWithRescuerKey(string containerPath, string rescuerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        if (!global::FileAction.Core.Lock.RescuerKey.TryParse(rescuerKey, out var canonical))
            return false;
        try
        {
            using var source = new FileStream(LockPaths.Normalize(containerPath), FileMode.Open, FileAccess.Read,
                FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
            var header = LockContainer.ReadHeader(source);
            if (!LockContainer.TryUnwrapRole(header, LockSecretKind.RescuerKey, canonical, out var dataKey))
                return false;
            LockSecrets.Wipe(dataKey);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LockException
                                       or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryRescuerSecret(LockHeader header, string typed, out LockSecretKind kind)
    {
        kind = LockSecretKind.FilePassword;
        if (LockContainer.TryUnwrapRole(header, LockSecretKind.RescuerPassword, typed, out var passwordKey))
        {
            LockSecrets.Wipe(passwordKey);
            kind = LockSecretKind.RescuerPassword;
            return true;
        }
        if (LockContainer.TryUnwrapRole(header, LockSecretKind.RescuerKey, typed, out var keyKey))
        {
            LockSecrets.Wipe(keyKey);
            kind = LockSecretKind.RescuerKey;
            return true;
        }
        return false;
    }

    private static bool Opens(LockHeader header, string password, string canonicalKey)
    {
        if (!LockContainer.TryUnwrapRole(header, LockSecretKind.RescuerPassword, password, out var passwordKey))
            return false;
        LockSecrets.Wipe(passwordKey);
        if (!LockContainer.TryUnwrapRole(header, LockSecretKind.RescuerKey, canonicalKey, out var keyKey))
            return false;
        LockSecrets.Wipe(keyKey);
        return true;
    }
}
