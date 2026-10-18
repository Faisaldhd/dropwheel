using System.Runtime.InteropServices;

namespace FileAction.App.Diagnostics;

/// <summary>
/// R-21: binaries built inside the low-integrity build sandbox start as LOW-integrity processes. Such a process
/// cannot write to <c>%TEMP%</c>/<c>%APPDATA%</c> and cannot receive input from normal windows, which looks
/// exactly like "the app opens and disappears". Detect it and say so instead of failing silently.
/// </summary>
internal static class Integrity
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int MediumRid = 0x2000;

    /// <summary>The integrity RID (0x1000 low, 0x2000 medium, 0x3000 high…), or null when unknown.</summary>
    public static int? Level => ReadLevel();

    /// <summary>True when the process runs below medium integrity (low or untrusted).</summary>
    public static bool IsLow => Level is int rid && rid < MediumRid;

    public static string Describe() => Level switch
    {
        null => "unknown",
        < 0x1000 => $"untrusted (0x{Level:X4})",
        < MediumRid => $"low (0x{Level:X4})",
        < 0x3000 => $"medium (0x{Level:X4})",
        < 0x4000 => $"high (0x{Level:X4})",
        _ => $"system (0x{Level:X4})",
    };

    private static int? ReadLevel()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
                return null;

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var size);
            if (size <= 0)
                return null;

            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
                    return null;

                var label = Marshal.PtrToStructure<SidAndAttributes>(buffer);
                if (label.Sid == IntPtr.Zero)
                    return null;

                var countPointer = GetSidSubAuthorityCount(label.Sid);
                if (countPointer == IntPtr.Zero)
                    return null;
                var count = Marshal.ReadByte(countPointer);
                if (count == 0)
                    return null;

                var ridPointer = GetSidSubAuthority(label.Sid, (uint)(count - 1));
                return ridPointer == IntPtr.Zero ? null : Marshal.ReadInt32(ridPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return null;
        }
        finally
        {
            if (token != IntPtr.Zero)
                CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint length, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
