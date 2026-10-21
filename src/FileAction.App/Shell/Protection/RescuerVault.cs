using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
/// Current-user DPAPI: the blob can only be read on this Windows account on this machine. No prompt is
/// ever shown, and the description never contains a secret.
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
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
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
                LocalFree(output.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
        }
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
/// the current user, so another account or a copied file reveals nothing. A missing or unreadable vault
/// never throws: the app keeps the material in memory for this session and asks again next start.
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
            if (!File.Exists(_path))
                return null;
            var protectedBytes = File.ReadAllBytes(_path);
            var plain = _protector.Unprotect(protectedBytes);
            if (plain is null)
                return null;
            try
            {
                var secrets = RescuerVaultCodec.TryDecode(plain);
                if (secrets is null)
                    return null;
                _session = secrets;
                Persistent = true;
                return secrets;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
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

    public void Save(RescuerSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (!RescuerKey.TryParse(secrets.Key, out var canonical))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(secrets));
        var normalized = new RescuerSecrets(canonical, secrets.Password, secrets.KeyUsed);
        _session = normalized;
        _fallback.Save(normalized);
        byte[]? plain = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            plain = RescuerVaultCodec.Encode(normalized);
            var protectedBytes = _protector.Protect(plain);
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
            Persistent = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or DllNotFoundException or EntryPointNotFoundException
                                       or PlatformNotSupportedException or NotSupportedException
                                       or PathTooLongException or System.Security.SecurityException
                                       or ArgumentException)
        {
            // The session copy still works; the next start behaves like a first run. Never degrade to a
            // plaintext file and never log the failure: the app must not write a secret to disk or a log.
            Persistent = false;
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
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
