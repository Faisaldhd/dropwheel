using System.Security.Cryptography;

namespace FileAction.Core.Lock;

/// <summary>
/// The pure rules behind the final Rescuer UX: a provisional pair for the very first Lock (before any
/// Rescuer exists), proof by password or key, and the "key must have opened a file once" gate on rotation.
/// No file or registry work happens here; the app layer supplies the persistence.
/// </summary>
public static class RescuerPolicy
{
    /// <summary>
    /// The temporary pair used when a file is protected before a Rescuer exists. The password is random and
    /// is never shown; after the automatic Protection-settings step the container's slots are replaced with
    /// the user's pair. Cancelling that step leaves the file openable by its file password only.
    /// </summary>
    public static RescuerSecrets CreateProvisional()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        try
        {
            return new RescuerSecrets(RescuerKey.Generate(), Convert.ToHexString(bytes), KeyUsed: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>True only after the current key has opened a protected file at least once.</summary>
    public static bool CanCreateNewKey(RescuerSecrets? stored) =>
        stored is { KeyUsed: true } && !string.IsNullOrEmpty(stored.Key) && !string.IsNullOrEmpty(stored.Password);

    /// <summary>Creates the replacement key and marks it unused; the password is carried over.</summary>
    public static RescuerSecrets CreateNewKey(RescuerSecrets stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored with { Key = RescuerKey.Generate(), KeyUsed = false };
    }

    /// <summary>True when the typed text is the stored Rescuer password.</summary>
    public static bool MatchesPassword(RescuerSecrets? stored, string? typed) =>
        stored is not null && !string.IsNullOrEmpty(stored.Password) && LockSecrets.SameSecret(stored.Password, typed);

    /// <summary>
    /// True when the typed text is the stored Rescuer key, accepted in the grouped display form too.
    /// </summary>
    public static bool MatchesKey(RescuerSecrets? stored, string? typed)
    {
        if (stored is null || string.IsNullOrEmpty(stored.Key) || string.IsNullOrEmpty(typed))
            return false;
        if (!RescuerKey.TryParse(typed, out var canonical))
            return false;
        return LockSecrets.SameSecret(stored.Key, canonical);
    }

    /// <summary>
    /// Proves a Rescuer password or key. The kind is returned because opening a file with the key is what
    /// marks the key as used; a password never does.
    /// </summary>
    public static bool TryMatch(RescuerSecrets? stored, string? typed, out LockSecretKind kind)
    {
        if (MatchesPassword(stored, typed))
        {
            kind = LockSecretKind.RescuerPassword;
            return true;
        }
        if (MatchesKey(stored, typed))
        {
            kind = LockSecretKind.RescuerKey;
            return true;
        }
        kind = LockSecretKind.FilePassword;
        return false;
    }

    /// <summary>The three secrets one Protect call needs, built from the stored pair and the file password.</summary>
    public static LockCredentials Credentials(string filePassword, RescuerSecrets rescuer)
    {
        ArgumentNullException.ThrowIfNull(rescuer);
        if (!rescuer.IsComplete)
            throw new ArgumentException("The Rescuer credentials are incomplete.", nameof(rescuer));
        return new LockCredentials
        {
            FilePassword = filePassword,
            RescuerPassword = rescuer.Password!,
            RescuerKey = rescuer.Key,
        };
    }
}
