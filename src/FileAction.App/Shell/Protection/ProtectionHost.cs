using System.IO;
using System.Runtime.InteropServices;
using FileAction.Core.Lock;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The Windows side of protection: Recycle Bin removal for the encrypted container after a verified
/// unlock (never for the plaintext), and the shell refresh that makes a restored file or folder appear
/// immediately with its normal icon.
/// </summary>
internal sealed class ProtectionHost : ILockHost
{
    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    public void RemoveContainer(string protectedPath, bool recycleBin)
    {
        if (!File.Exists(protectedPath))
            return;
        File.SetAttributes(protectedPath, FileAttributes.Normal);
        if (recycleBin)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(protectedPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                NotifyShell(protectedPath);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Fall through to a normal delete; the plaintext is already verified.
            }
        }
        File.Delete(protectedPath);
        NotifyShell(protectedPath);
    }

    public bool IsSessionOpen(string protectedPath) =>
        LockRuntime.IsInitialized && LockRuntime.Journal.FindByContainer(protectedPath) is not null;

    public void NotifyShell(string path)
    {
        try
        {
            // One association-changed notification makes Explorer reload badges, icons and desktop.ini
            // without keeping an unmanaged copy of the path alive.
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
