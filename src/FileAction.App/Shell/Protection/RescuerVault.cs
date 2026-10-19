using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileAction.Core.Lock;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// Stores the Rescuer key and password for the current Windows user with DPAPI. The blob lives in
/// <c>%LOCALAPPDATA%\DropWheel\protection</c>, never in settings, a log, a temp file or a crash dump, and
/// another user or machine cannot read it. When DPAPI is unavailable the material stays in memory for the
/// session only.
/// </summary>
internal sealed class DpapiRescuerVault : IRescuerVault
{
    private const int CryptProtectUiForbidden = 0x1;
    private const int Version = 1;

    private readonly InMemoryRescuerVault _fallback = new();

    public RescuerSecrets? Load()
    {
        try
        {
            if (!File.Exists(ProtectionPaths.RescuerVaultFile))
                return _fallback.Load();
            var protectedBytes = File.ReadAllBytes(ProtectionPaths.RescuerVaultFile);
            var plain = Unprotect(protectedBytes);
            if (plain is null)
                return _fallback.Load();
            try
            {
                var text = Encoding.UTF8.GetString(plain);
                string? key = null;
                string? password = null;
                foreach (var line in text.Split('\n'))
                {
                    var equals = line.IndexOf('=');
                    if (equals <= 0)
                        continue;
                    var name = line[..equals].Trim();
                    var value = line[(equals + 1)..].Trim();
                    if (name == "key")
                        key = value;
                    else if (name == "password" && value.Length > 0)
                        password = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                }
                if (key is null || !RescuerKey.TryParse(key, out var canonical))
                    return _fallback.Load();
                return new RescuerSecrets(canonical, password);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                                       or CryptographicException or DllNotFoundException)
        {
            return _fallback.Load();
        }
    }

    public void Save(RescuerSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (!RescuerKey.TryParse(secrets.Key, out var canonical))
            throw new ArgumentException("The Rescuer key is not valid.", nameof(secrets));
        _fallback.Save(new RescuerSecrets(canonical, secrets.Password));
        try
        {
            Directory.CreateDirectory(ProtectionPaths.ProtectionFolder);
            var text = new StringBuilder();
            text.Append("version=").Append(Version).Append('\n');
            text.Append("key=").Append(canonical).Append('\n');
            if (!string.IsNullOrEmpty(secrets.Password))
                text.Append("password=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(secrets.Password)))
                    .Append('\n');
            var plain = Encoding.UTF8.GetBytes(text.ToString());
            try
            {
                var protectedBytes = Protect(plain);
                var temp = ProtectionPaths.RescuerVaultFile + ".tmp";
                File.WriteAllBytes(temp, protectedBytes);
                File.Move(temp, ProtectionPaths.RescuerVaultFile, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or DllNotFoundException)
        {
            // The session copy still works; the user is asked again next start.
        }
    }

    public void Clear()
    {
        _fallback.Clear();
        try
        {
            if (File.Exists(ProtectionPaths.RescuerVaultFile))
                File.Delete(ProtectionPaths.RescuerVaultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ DPAPI

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, string? description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Protect(byte[] plain)
    {
        var input = new DataBlob { Size = plain.Length, Data = Marshal.AllocHGlobal(plain.Length) };
        try
        {
            Marshal.Copy(plain, 0, input.Data, plain.Length);
            if (!CryptProtectData(ref input, "DropWheel Rescuer", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
                throw new CryptographicException("DPAPI could not protect the Rescuer material.");
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

    private static byte[]? Unprotect(byte[] protectedBytes)
    {
        var input = new DataBlob { Size = protectedBytes.Length, Data = Marshal.AllocHGlobal(protectedBytes.Length) };
        try
        {
            Marshal.Copy(protectedBytes, 0, input.Data, protectedBytes.Length);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
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
}
