using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using FileAction.Core.Lock;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The Windows surface the Rescuer vault needs. It is an interface so the failure and round-trip paths can
/// be tested without depending on the test runner's DPAPI profile.
/// </summary>
internal interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[]? Unprotect(byte[] protectedBytes);
}

/// <summary>
/// Current-user DPAPI. It protects the blob at rest against another Windows account, an offline disk or a
/// copied file. It is deliberately NOT a security boundary against code running as the same Windows user:
/// such code can call <c>CryptUnprotectData</c> with exactly the same rights as DropWheel. Do not describe
/// this as protection from same-user malware; the Rescuer key kept outside the PC is the recovery boundary.
/// No prompt is ever shown, and the description never contains a secret.
/// </summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    public static readonly DpapiSecretProtector Instance = new();

    private const int UiForbidden = 0x1;

    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var input = new DataBlob { Size = plaintext.Length, Data = Marshal.AllocHGlobal(plaintext.Length) };
        try
        {
            Marshal.Copy(plaintext, 0, input.Data, plaintext.Length);
            if (!CryptProtectData(ref input, "DropWheel Rescuer credentials", IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, UiForbidden, out var output))
                throw new CryptographicException("DPAPI could not protect the Rescuer credentials.");
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                ZeroAndLocalFree(ref output);
            }
        }
        finally
        {
            // The buffer holds the plaintext Rescuer password and key. Clear it with the non-elidable
            // kernel primitive before the allocator can reuse it.
            ZeroAndFreeHGlobal(ref input, plaintext.Length);
        }
    }

    public byte[]? Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);
        if (protectedBytes.Length == 0)
            return null;
        var input = new DataBlob { Size = protectedBytes.Length, Data = Marshal.AllocHGlobal(protectedBytes.Length) };
        try
        {
            Marshal.Copy(protectedBytes, 0, input.Data, protectedBytes.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden,
                    out var output))
                return null;
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally
            {
                // DPAPI just produced plaintext in this unmanaged block; wipe it before LocalFree.
                ZeroAndLocalFree(ref output);
            }
        }
        finally
        {
            ZeroAndFreeHGlobal(ref input, protectedBytes.Length);
        }
    }

    private static void ZeroAndFreeHGlobal(ref DataBlob blob, int length)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        if (length > 0)
            RtlZeroMemory(blob.Data, (UIntPtr)(uint)length);
        Marshal.FreeHGlobal(blob.Data);
        blob.Data = IntPtr.Zero;
        blob.Size = 0;
    }

    private static void ZeroAndLocalFree(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        if (blob.Size > 0)
            RtlZeroMemory(blob.Data, (UIntPtr)(uint)blob.Size);
        LocalFree(blob.Data);
        blob.Data = IntPtr.Zero;
        blob.Size = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy,
        IntPtr reserved, IntPtr promptStruct, int flags, out DataBlob dataOut);

    // Windows does not export the SecureZeroMemory macro; its non-elidable kernel primitive is
    // RtlZeroMemory, which is what this call reaches. It cannot be optimized away like a managed loop.
    [DllImport("ntdll.dll", EntryPoint = "RtlZeroMemory")]
    private static extern void RtlZeroMemory(IntPtr memory, UIntPtr length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

/// <summary>
/// Line format inside the DPAPI blob. Version 1 is the 2.4.0 shape (no KeyUsed); version 2 adds the flag.
/// The bytes never touch a file before DPAPI wraps them, and the password is base64 so no line can break
/// the format.
/// </summary>
internal static class RescuerVaultCodec
{
    private const int Version = 2;
    private const string PasswordMarker = "password=";
    private const string KeyUsedMarker = "keyUsed=";
    private const string KeyMarker = "key=";
    private const string VersionMarker = "version=";

    public static byte[] Encode(RescuerSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (!RescuerKey.TryParse(secrets.Key, out var canonical))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(secrets));
        var text = new StringBuilder();
        text.Append(VersionMarker).Append(Version).Append('\n');
        text.Append(KeyMarker).Append(canonical).Append('\n');
        if (!string.IsNullOrEmpty(secrets.Password))
            text.Append(PasswordMarker)
                .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(secrets.Password)))
                .Append('\n');
        text.Append(KeyUsedMarker).Append(secrets.KeyUsed ? '1' : '0').Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>Parses a blob; null when it is not a valid Rescuer record.</summary>
    public static RescuerSecrets? TryDecode(ReadOnlySpan<byte> plaintext)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(plaintext);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        string? key = null;
        string? password = null;
        var keyUsed = false;
        foreach (var line in text.Split('\n'))
        {
            var equals = line.IndexOf('=');
            if (equals <= 0)
                continue;
            var name = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            switch (name)
            {
                case "key":
                    key = value;
                    break;
                case "password" when value.Length > 0:
                    try
                    {
                        password = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                    break;
                case "keyUsed":
                    keyUsed = value is "1" or "true" or "True";
                    break;
                case "version":
                    if (!int.TryParse(value, out var version) || version is < 1 or > Version)
                        return null;
                    break;
            }
        }
        if (key is null || !RescuerKey.TryParse(key, out var canonical))
            return null;
        return new RescuerSecrets(canonical, password, keyUsed);
    }
}

/// <summary>
/// Where the Rescuer password, key and KeyUsed flag live between starts. Windows DPAPI seals the blob for
/// the current user, so another account or a copied file reveals nothing. This is a data-at-rest measure,
/// not a boundary against code running as this same user (see <see cref="DpapiSecretProtector"/>). A
/// missing or unreadable vault never throws: the app asks again next start. A failed Save is reported to
/// the caller instead of being hidden, and a save only reports success after the sealed blob was reloaded
/// and compared with the requested pair.
/// </summary>
internal sealed class DpapiRescuerVault : IRescuerVault
{
    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly InMemoryRescuerVault _fallback = new();
    private RescuerSecrets? _session;

    /// <summary>True when the last successful Save/Load used DPAPI and wrote/read the file.</summary>
    public bool Persistent { get; private set; }

    public DpapiRescuerVault()
        : this(ProtectionPaths.RescuerVaultFile, DpapiSecretProtector.Instance)
    {
    }

    internal DpapiRescuerVault(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public RescuerSecrets? Load()
    {
        if (_session is not null)
            return _session;
        try
        {
            var secrets = ReadPersisted();
            if (secrets is null)
                return _fallback.Load();
            _session = secrets;
            Persistent = true;
            return secrets;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or DllNotFoundException or EntryPointNotFoundException
                                       or PlatformNotSupportedException or NotSupportedException
                                       or PathTooLongException or System.Security.SecurityException
                                       or ArgumentException)
        {
            // No logging: an exception here can carry environment details, never a secret. The app simply
            // behaves as if this were a first run and falls back to the process-lifetime copy.
            return _fallback.Load();
        }
    }

    /// <summary>
    /// Saves the pair and returns true only when the new blob was written durably, atomically installed,
    /// given an explicit current-user + SYSTEM DACL, and read back with exactly the same values. On any
    /// failure the previous blob is restored (when one existed), the session copy is left untouched, and
    /// false is returned so the caller can keep its dialog open and warn instead of claiming success.
    /// </summary>
    public bool Save(RescuerSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (!RescuerKey.TryParse(secrets.Key, out var canonical))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(secrets));
        var normalized = new RescuerSecrets(canonical,
            string.IsNullOrEmpty(secrets.Password) ? null : secrets.Password, secrets.KeyUsed);
        var temp = _path + ".tmp";
        var backup = _path + ".bak";
        byte[]? plain = null;
        var hadExisting = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            hadExisting = File.Exists(_path);
            RecoverOrClearStale(temp, backup);
            hadExisting = File.Exists(_path);
            plain = RescuerVaultCodec.Encode(normalized);
            var protectedBytes = _protector.Protect(plain);
            WriteProtectedBlob(temp, protectedBytes);
            if (hadExisting)
            {
                File.Replace(temp, _path, backup, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(temp, _path);
            }
            ApplyVaultAcl(_path);
            if (!Matches(ReadPersisted(), normalized))
            {
                RestorePrevious(backup, hadExisting);
                TryDelete(temp);
                return false;
            }
            TryDelete(temp);
            TryDelete(backup);
            _session = normalized;
            _fallback.Save(normalized);
            Persistent = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or DllNotFoundException or EntryPointNotFoundException
                                       or PlatformNotSupportedException or NotSupportedException
                                       or PathTooLongException or System.Security.SecurityException
                                       or ArgumentException or PrivilegeNotHeldException)
        {
            // Never degrade to a plaintext file and never log the failure: a secret must not reach a log.
            // Put the previous sealed blob back so a caller that keeps the old session copy still matches
            // what is on disk.
            // Keep the pair for this process only; the configured marker is NOT set by the caller unless
            // Save returned true. This preserves same-session operation while never claiming persistence.
            _session = normalized;
            _fallback.Save(normalized);
            RestorePrevious(backup, hadExisting);
            TryDelete(temp);
            return false;
        }
        finally
        {
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Clear()
    {
        _session = null;
        _fallback.Clear();
        Persistent = false;
        TryDelete(_path + ".tmp");
        TryDelete(_path + ".bak");
        TryDelete(_path);
    }

    private RescuerSecrets? ReadPersisted()
    {
        if (!File.Exists(_path))
            return null;
        var protectedBytes = File.ReadAllBytes(_path);
        var plain = _protector.Unprotect(protectedBytes);
        if (plain is null)
            return null;
        try
        {
            return RescuerVaultCodec.TryDecode(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static bool Matches(RescuerSecrets? loaded, RescuerSecrets expected)
    {
        if (loaded is null)
            return false;
        if (!LockSecrets.SameSecret(loaded.Key, expected.Key))
            return false;
        if (!string.Equals(loaded.Password, expected.Password, StringComparison.Ordinal))
            return false;
        return loaded.KeyUsed == expected.KeyUsed;
    }

    /// <summary>Writes the sealed bytes with write-through and an explicit DACL from the first byte.</summary>
    private static void WriteProtectedBlob(string path, byte[] protectedBytes)
    {
        using var stream = FileSystemAclExtensions.Create(new FileInfo(path), FileMode.CreateNew,
            FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, VaultSecurity());
        stream.Write(protectedBytes, 0, protectedBytes.Length);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>Current user plus SYSTEM only, inheritance removed: the smallest set that can read it.</summary>
    private static FileSecurity VaultSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                AccessControlType.Allow));
        }
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private static void ApplyVaultAcl(string path) =>
        new FileInfo(path).SetAccessControl(VaultSecurity());

    /// <summary>
    /// Deals with temp/backup files from an interrupted earlier save. A stale backup is restored when the
    /// destination is missing; otherwise both artifacts are ours and safe to remove. Only the exact
    /// sibling names are touched, never a pattern or a caller-supplied path.
    /// </summary>
    private void RecoverOrClearStale(string temp, string backup)
    {
        if (File.Exists(_path))
        {
            TryDelete(temp);
            TryDelete(backup);
            return;
        }
        if (File.Exists(backup))
        {
            // The previous save installed the new blob but crashed before deleting the backup and the
            // destination later disappeared. The backup is the only sealed copy; put it back.
            File.Move(backup, _path);
            try
            {
                ApplyVaultAcl(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or System.Security.SecurityException or ArgumentException
                                           or PrivilegeNotHeldException)
            {
                // The caller's next Save will install a fresh blob and set the DACL again.
            }
        }
        TryDelete(temp);
    }

    /// <summary>Puts the previous blob back after a failed install; best effort, never throws.</summary>
    private void RestorePrevious(string backup, bool hadExisting)
    {
        try
        {
            if (hadExisting && File.Exists(backup))
            {
                if (File.Exists(_path))
                    File.Replace(backup, _path, null, ignoreMetadataErrors: true);
                else
                    File.Move(backup, _path);
                ApplyVaultAcl(_path);
                TryDelete(backup);
            }
            else if (!hadExisting)
            {
                TryDelete(_path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or System.Security.SecurityException or ArgumentException
                                       or PrivilegeNotHeldException)
        {
            // A sealed backup left behind is still protected by DPAPI; the next Save clears it.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Security.SecurityException)
        {
        }
    }
}
