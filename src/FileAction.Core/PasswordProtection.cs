using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core;

/// <summary>Why a typed password cannot be used.</summary>
public enum PasswordProblem
{
    None,
    Empty,
    TooShort,
    TooLong,
    Mismatch,
}

/// <summary>
/// Password rules shared by the Protect and Unlock views. Nothing here stores, logs or hashes a password
/// for later use; the value only lives for the duration of one operation.
/// </summary>
public static class PasswordRules
{
    /// <summary>New archives and PDFs need at least this many characters.</summary>
    public const int MinimumLength = 8;

    /// <summary>A sanity limit: a longer value is almost always a paste accident.</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// Validates a password typed for a new archive. <paramref name="confirmation"/> is required when
    /// <paramref name="requireConfirmation"/> is true.
    /// </summary>
    public static PasswordProblem Validate(string? password, string? confirmation = null, bool requireConfirmation = false)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordProblem.Empty;
        if (password.Length < MinimumLength)
            return PasswordProblem.TooShort;
        if (password.Length > MaximumLength)
            return PasswordProblem.TooLong;
        if (requireConfirmation && !string.Equals(password, confirmation, StringComparison.Ordinal))
            return PasswordProblem.Mismatch;
        return PasswordProblem.None;
    }

    /// <summary>
    /// Validation for an existing archive: any non-empty password is accepted (the archive, not this app,
    /// decides whether it is right), but an accidental huge paste is still refused.
    /// </summary>
    public static PasswordProblem ValidateForUnlock(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordProblem.Empty;
        return password.Length > MaximumLength ? PasswordProblem.TooLong : PasswordProblem.None;
    }

    /// <summary>The localization key that explains a problem, or null when there is none.</summary>
    public static string? MessageKey(PasswordProblem problem) => problem switch
    {
        PasswordProblem.Empty or PasswordProblem.TooShort => "tool.protect.error.password",
        PasswordProblem.TooLong => "tool.protect.error.tooLong",
        PasswordProblem.Mismatch => "tool.protect.error.mismatch",
        _ => null,
    };
}

/// <summary>A coarse, deliberately simple strength estimate for the meter under the password box.</summary>
public enum PasswordStrength
{
    VeryWeak = 0,
    Weak = 1,
    Fair = 2,
    Strong = 3,
}

/// <summary>
/// Length and character-class heuristic. It is not an entropy claim and never leaves the UI; the value is
/// computed in memory and discarded when the view closes.
/// </summary>
public static class PasswordStrengthMeter
{
    public static PasswordStrength Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordStrength.VeryWeak;

        var classes = 0;
        if (password.Any(char.IsLower))
            classes++;
        if (password.Any(char.IsUpper))
            classes++;
        if (password.Any(char.IsDigit))
            classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c)))
            classes++;

        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (password.Length >= 20) score++;
        if (classes >= 2) score++;
        if (classes >= 3) score++;
        if (classes >= 4) score++;

        // "aaaaaaaa" or "12341234" is not strong however long it is.
        if (password.Distinct().Count() <= 2)
            score = 0;

        return score switch
        {
            <= 1 => PasswordStrength.VeryWeak,
            2 or 3 => PasswordStrength.Weak,
            4 or 5 => PasswordStrength.Fair,
            _ => PasswordStrength.Strong,
        };
    }

    /// <summary>Localization key for the meter label.</summary>
    public static string LabelKey(PasswordStrength strength) => strength switch
    {
        PasswordStrength.VeryWeak => "tool.protect.strength.veryWeak",
        PasswordStrength.Weak => "tool.protect.strength.weak",
        PasswordStrength.Fair => "tool.protect.strength.fair",
        _ => "tool.protect.strength.strong",
    };
}

/// <summary>
/// The documented key-derivation plan for WinZip AES entries. The iteration count is fixed by the WinZip
/// AES specification; changing it would make the archive unreadable by other tools.
/// </summary>
public readonly record struct ZipAesKeyDerivationPlan(
    int Iterations,
    HashAlgorithmName Hash,
    int KeyBits,
    int SaltBytes)
{
    public static ZipAesKeyDerivationPlan Default => new(
        Iterations: 1000,
        Hash: HashAlgorithmName.SHA1,
        KeyBits: 256,
        SaltBytes: 16);

    public int KeyBytes => KeyBits / 8;

    /// <summary>Bytes needed from PBKDF2: encryption key, authentication key, 2-byte verifier.</summary>
    public int DerivedBytes => KeyBytes * 2 + 2;
}

/// <summary>One derived AES entry key set. Call <see cref="Dispose"/> as soon as the entry is written.</summary>
public readonly record struct ZipAesKeyMaterial(byte[] EncryptionKey, byte[] AuthenticationKey, byte[] PasswordVerifier)
    : IDisposable
{
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(EncryptionKey);
        CryptographicOperations.ZeroMemory(AuthenticationKey);
        CryptographicOperations.ZeroMemory(PasswordVerifier);
    }
}

/// <summary>Derives WinZip AES-256 key material from a password and a per-entry salt.</summary>
public static class ZipAesKeyDerivation
{
    public static ZipAesKeyMaterial Derive(string password, ReadOnlySpan<byte> salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var plan = ZipAesKeyDerivationPlan.Default;
        if (salt.Length != plan.SaltBytes)
            throw new ArgumentException("The AES salt must be 16 bytes for AES-256.", nameof(salt));

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[] material;
        try
        {
            material = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes, salt.ToArray(), plan.Iterations, plan.Hash, plan.DerivedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        try
        {
            var key = material.AsSpan(0, plan.KeyBytes).ToArray();
            var authentication = material.AsSpan(plan.KeyBytes, plan.KeyBytes).ToArray();
            var verifier = material.AsSpan(plan.KeyBytes * 2, 2).ToArray();
            return new ZipAesKeyMaterial(key, authentication, verifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }
}

/// <summary>What this app can actually do, so the UI never promises more than the container allows.</summary>
public static class PasswordProtectionCapabilities
{
    public const int ZipAesKeyBits = 256;

    /// <summary>ZIP has no name-encryption field; a viewer can list names without the password.</summary>
    public const bool ZipEncryptsEntryNames = false;

    public const int PdfAesKeyBits = 256;

    /// <summary>PDF owner permissions (print/copy) are written by PDFsharp.</summary>
    public const bool PdfSupportsOwnerPermissions = true;
}

/// <summary>Owner restrictions the user can request for a protected PDF.</summary>
public readonly record struct PdfPermissionPolicy(bool AllowPrinting, bool AllowCopying)
{
    public static PdfPermissionPolicy AllowAll => new(true, true);
}

/// <summary>One file or directory that will become an entry in the protected archive.</summary>
public readonly record struct ProtectedZipEntry(
    string SourcePath,
    string EntryName,
    bool IsDirectory,
    long Length);

/// <summary>All entries collected before the output exists, so a partial archive is never an input.</summary>
public sealed record ProtectedZipPlan(IReadOnlyList<ProtectedZipEntry> Entries, int SkippedLinks);

/// <summary>
/// Builds the deterministic entry list for a protect operation: a single folder is stored under its own
/// name, files keep their names, symbolic links and junctions are skipped, and duplicate top-level names
/// are disambiguated.
/// </summary>
public static class ProtectedZipPlanBuilder
{
    /// <summary>ZIP32's largest representable size. Zip64 is intentionally not implemented.</summary>
    public const long MaximumEntrySize = uint.MaxValue - 65536;

    public static ProtectedZipPlan Collect(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count == 0)
            throw new PasswordProtectionException(
                PasswordProtectionFailure.Empty, "tool.protect.error.empty");

        var entries = new List<ProtectedZipEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root))
                throw Unsupported();

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(root);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw Unsupported(ex);
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                throw Unsupported(ex);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                skipped++;
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                var folderName = new DirectoryInfo(fullPath).Name;
                if (string.IsNullOrEmpty(folderName))
                    folderName = "root";
                var entryName = Unique($"{folderName}/", names, directory: true);
                entries.Add(new ProtectedZipEntry(fullPath, entryName, IsDirectory: true, Length: 0));
                Walk(fullPath, entryName, entries, names, ref skipped, cancellationToken);
            }
            else
            {
                AddFile(fullPath, Path.GetFileName(fullPath), entries, names);
            }
        }

        if (entries.Count == 0)
            throw new PasswordProtectionException(
                PasswordProtectionFailure.Empty, "tool.protect.error.empty");

        return new ProtectedZipPlan(entries, skipped);
    }

    private static void Walk(
        string directory,
        string prefix,
        List<ProtectedZipEntry> entries,
        HashSet<string> names,
        ref int skipped,
        CancellationToken cancellationToken)
    {
        IEnumerable<FileSystemInfo> children;
        try
        {
            children = new DirectoryInfo(directory).EnumerateFileSystemInfos()
                .OrderBy(child => child.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unsupported(ex);
        }

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileAttributes attributes;
            try
            {
                attributes = child.Attributes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw Unsupported(ex);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                skipped++;
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                var entryName = Unique(prefix + child.Name + "/", names, directory: true);
                entries.Add(new ProtectedZipEntry(child.FullName, entryName, IsDirectory: true, Length: 0));
                Walk(child.FullName, entryName, entries, names, ref skipped, cancellationToken);
            }
            else
            {
                AddFile(child.FullName, prefix + child.Name, entries, names);
            }
        }
    }

    private static void AddFile(string path, string name, List<ProtectedZipEntry> entries, HashSet<string> names)
    {
        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Unsupported(ex);
        }

        if (length > MaximumEntrySize)
            throw new PasswordProtectionException(
                PasswordProtectionFailure.TooLarge, "tool.protect.error.tooLarge");

        entries.Add(new ProtectedZipEntry(path, Unique(name, names, directory: false), IsDirectory: false, length));
    }

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

    private static PasswordProtectionException Unsupported(Exception? inner = null) =>
        new(PasswordProtectionFailure.UnsupportedInput, "tool.protect.error.unsupported", inner);
}

/// <summary>The operation failed; the message is localized and never contains the password.</summary>
public enum PasswordProtectionFailure
{
    InvalidPassword,
    PasswordMismatch,
    TooLong,
    UnsupportedInput,
    Empty,
    TooLarge,
    AlreadyProtected,
    WrongPasswordOrCorrupt,
    NotEncrypted,
    Generic,
}

/// <summary>
/// A protect/unlock failure. The message comes from the string table so the shared tool window can show it
/// directly; no constructor accepts a password, which makes accidental logging impossible.
/// </summary>
public sealed class PasswordProtectionException : Exception
{
    public PasswordProtectionException(
        PasswordProtectionFailure failure,
        string messageKey,
        Exception? inner = null,
        params object?[] messageArguments)
        : base(FileAction.Core.Loc.T(messageKey, messageArguments), inner)
    {
        Failure = failure;
        MessageKey = messageKey;
    }

    public PasswordProtectionFailure Failure { get; }

    public string MessageKey { get; }
}

/// <summary>Result of a protect operation.</summary>
public sealed record ZipProtectionResult(int FileCount, int DirectoryCount, int SkippedLinks);

/// <summary>Result of an unlock operation.</summary>
public sealed record ZipUnlockResult(int FileCount);
