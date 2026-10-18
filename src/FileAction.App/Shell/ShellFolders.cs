using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using FileAction.App.Native;

namespace FileAction.App.Shell;

/// <summary>
/// Resolves the folder shown by the File Explorer window under a screen point, so a file can be
/// dropped "onto" another folder and have its result land there.
/// </summary>
internal static class ShellFolders
{
    public static string? FolderAt(Win32.POINT point)
    {
        object? shell = null;
        try
        {
            var hwnd = Win32.WindowFromPoint(point);
            if (hwnd == IntPtr.Zero)
                return null;
            var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
            if (root == IntPtr.Zero)
                root = hwnd;

            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null)
                return null;
            shell = Activator.CreateInstance(type);
            if (shell == null)
                return null;

            var windows = shell.GetType().InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null) as IEnumerable;
            if (windows == null)
                return null;

            foreach (var window in windows)
            {
                if (window == null)
                    continue;
                try
                {
                    var hwndValue = window.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, window, null);
                    if (hwndValue is not int id || new IntPtr(id) != root)
                        continue;

                    var document = window.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, window, null);
                    if (document == null)
                        continue;
                    var folder = document.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, document, null);
                    if (folder == null)
                        continue;
                    var self = folder.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
                    if (self == null)
                        continue;
                    var path = self.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null) as string;
                    return !string.IsNullOrEmpty(path) && Directory.Exists(path) ? path : null;
                }
                catch
                {
                    // not a shell browser, or it closed while we were asking
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell))
            {
                try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }
    }
}
