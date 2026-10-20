using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// Loads the real Explorer icon for a file type or folder at an explicit size through
/// IShellItemImageFactory. The shell returns the system's own artwork at 16-256 px; for a type-only
/// lookup a temporary empty file with that extension is used so the shell resolves the association.
/// </summary>
internal static class ShellIcon
{
    private const uint SIIGBF_BIGGERSIZEOK = 0x1;
    private const uint SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Width;
        public int Height;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    private static ImageSource? Fallback(string path)
    {
        try
        {
            return FileIcons.Get(path, large: true);
        }
        catch (Exception)
        {
            return null;
        }
    }
    /// <summary>The shell icon at the exact requested size, or the legacy 32 px fallback.</summary>
    public static ImageSource? Get(string path, int size, bool folder)
    {
        var temporary = default(string);
        try
        {
            var actual = path;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                if (folder)
                {
                    temporary = Path.Combine(Path.GetTempPath(), "dw-lockicon-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(temporary);
                }
                else
                {
                    temporary = Path.Combine(Path.GetTempPath(),
                        "dw-lockicon-" + Guid.NewGuid().ToString("N") + (Path.GetExtension(path) is { Length: > 0 } extension
                            ? extension
                            : ".dat"));
                    File.WriteAllBytes(temporary, []);
                }
                actual = temporary;
            }

            var id = typeof(IShellItemImageFactory).GUID;
            var create = SHCreateItemFromParsingName(actual, IntPtr.Zero, ref id, out var factory);
            if (create < 0 || factory is null)
                return Fallback(path);
            try
            {
                var hr = factory.GetImage(new SIZE { Width = size, Height = size },
                    SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var bitmap);
                if (hr < 0 || bitmap == IntPtr.Zero)
                    return Fallback(path);
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                    return image;
                }
                finally
                {
                    DeleteObject(bitmap);
                }
            }
            finally
            {
                if (factory is not null)
                    Marshal.ReleaseComObject(factory);
            }
        }
        catch (Exception)
        {
            // The shell item can be unavailable for synthetic paths, disconnected drives or a type with no
            // association. The lock UI must still render, so use the generic fallback and never throw.
            return Fallback(path);
        }
        finally
        {
            if (temporary is not null)
            {
                try
                {
                    if (Directory.Exists(temporary))
                        Directory.Delete(temporary, recursive: true);
                    else if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
