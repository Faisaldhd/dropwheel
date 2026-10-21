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
