using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using FileAction.App.Diagnostics;
using FileAction.App.Native;
using FileAction.Core;

namespace FileAction.App.Shell;

/// <summary>
/// Resolves the folder shown by the File Explorer tab under a screen point, so a file can be dropped
/// "onto" another folder and have its result land there. Windows 11 lists every tab of one Explorer
/// window as a separate shell window sharing the same top-level HWND, so the tab window under the
/// cursor decides which folder is really visible.
/// </summary>
internal static class ShellFolders
{
    private const string TabWindowClass = "ShellTabWindowClass";

    // SID_STopLevelBrowser and IID_IShellBrowser: the documented way from a shell window to its browser.
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object? result);
    }

    // IShellBrowser derives from IOleWindow, whose GetWindow is the first method after IUnknown.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowserWindow
    {
        [PreserveSig] int GetWindow(out IntPtr window);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    private static string? _lastLogged;

    /// <summary>Last detection, shown by --diagnose. Only the folder path itself is recorded.</summary>
    public static string LastDetection { get; private set; } = "(no drag since the app started)";

    /// <summary>Last output-folder decision (used / same as source / missing / not writable / none).</summary>
    public static string LastDecision { get; private set; } = "(none yet)";

    /// <summary>Records the final decision for this drop and puts it in the app log.</summary>
    public static void NoteDecision(string decision)
    {
        LastDecision = decision;
        AppLog.Info("drop folder decision: " + decision);
    }

    /// <summary>The folder of the Explorer tab under <paramref name="point"/>, or null.</summary>
    /// <param name="point">Screen point of the cursor.</param>
    public static string? FolderAt(Win32.POINT point)
    {
        var detection = Detect(point);
        LastDetection = detection.Log;
        if (!string.Equals(_lastLogged, detection.Log, StringComparison.Ordinal))
        {
            _lastLogged = detection.Log;
            AppLog.Info(detection.Log);
        }
        return detection.Path;
    }

    private readonly record struct Detection(string? Path, string Log);

    private static Detection Detect(Win32.POINT point)
    {
        var cursor = $"cursor=({point.X},{point.Y})";
        var hwnd = Win32.WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return new Detection(null, $"drop target: {cursor} no window under the cursor");

        var root = Win32.GetAncestor(hwnd, Win32.GA_ROOT);
        if (root == IntPtr.Zero)
            root = hwnd;
        var windowClass = ClassOf(hwnd);
        var tab = AncestorOfClass(hwnd, TabWindowClass);
        var tabHandle = (tab ?? IntPtr.Zero).ToInt64();
        var rootHandle = root.ToInt64();

        object? shell = null;
        var candidates = new List<ShellFolderCandidate>();
        var described = new List<string>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null)
                return new Detection(null, $"drop target: {cursor} class={windowClass} Shell.Application unavailable");
            shell = Activator.CreateInstance(type);
            var collection = shell?.GetType().InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
            if (collection is not IEnumerable windows)
            {
                ReleaseCom(collection);
                return new Detection(null, $"drop target: {cursor} class={windowClass} no Shell.Windows collection");
            }

            try
            {
                foreach (var window in windows)
                {
                    if (window is null)
                        continue;
                    try
                    {
                        var id = ReadHandle(window);
                        var browserHandle = BrowserHandle(window) ?? id;
                        var path = ReadFolderPath(window);
                        if (string.IsNullOrEmpty(path) || !IsUnderRoot(browserHandle, root))
                            continue;

                        candidates.Add(new ShellFolderCandidate(path, browserHandle.ToInt64()));
                        var kind = browserHandle.ToInt64() == tabHandle ? "tab"
                            : browserHandle == root ? "root"
                            : "other";
                        described.Add($"{path} (0x{browserHandle.ToInt64():X},{kind})");
                    }
                    catch
                    {
                        // Not a shell browser, or the window closed while we were asking.
                    }
                    finally
                    {
                        ReleaseCom(window);
                    }
                }
            }
            finally
            {
                ReleaseCom(collection);
            }
        }
        catch
        {
            return new Detection(null, $"drop target: {cursor} class={windowClass} shell enumeration failed");
        }
        finally
        {
            ReleaseCom(shell);
        }

        var pick = ShellFolderSelection.Pick(candidates, tabHandle, rootHandle);
        var chosen = pick == ShellFolderSelection.None ? null : candidates[pick].Path;
        var reason = pick == ShellFolderSelection.None ? "no-matching-tab"
            : candidates[pick].WindowHandle == tabHandle ? "tab"
            : candidates[pick].WindowHandle == rootHandle ? "root-window"
            : "single-candidate";

        var log = $"drop target: {cursor} class={windowClass} root=0x{rootHandle:X} tab=0x{tabHandle:X} " +
                  $"entries={candidates.Count} candidates=[{string.Join("; ", described)}] " +
                  (chosen is null ? "chosen=none" : $"chosen={chosen}") + $" reason={reason}";
        return new Detection(chosen, log);
    }

    private static IntPtr ReadHandle(object window)
    {
        var value = window.GetType().InvokeMember("HWND", BindingFlags.GetProperty, null, window, null);
        return value is int id ? new IntPtr(id) : IntPtr.Zero;
    }

    /// <summary>The tab window of a shell entry, via IServiceProvider → IShellBrowser.GetWindow.</summary>
    private static IntPtr? BrowserHandle(object window)
    {
        object? browser = null;
        try
        {
            if (window is not IServiceProvider provider)
                return null;
            var service = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            if (provider.QueryService(ref service, ref iid, out browser) != 0 || browser is null)
                return null;
            return browser is IShellBrowserWindow shellBrowser && shellBrowser.GetWindow(out var hwnd) == 0
                ? hwnd
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(browser);
        }
    }

    private static string? ReadFolderPath(object window)
    {
        object? document = null;
        object? folder = null;
        object? self = null;
        try
        {
            document = window.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, window, null);
            folder = document?.GetType().InvokeMember("Folder", BindingFlags.GetProperty, null, document, null);
            self = folder?.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
            var path = self?.GetType().InvokeMember("Path", BindingFlags.GetProperty, null, self, null) as string;
            if (string.IsNullOrEmpty(path))
                return null;
            return Path.IsPathFullyQualified(path) && Directory.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(self);
            ReleaseCom(folder);
            ReleaseCom(document);
        }
    }

    private static bool IsUnderRoot(IntPtr window, IntPtr root)
    {
        if (window == IntPtr.Zero || root == IntPtr.Zero)
            return false;
        if (window == root || IsChild(root, window))
            return true;

        // A tab may hang below an intermediate host window: walk the parent chain once.
        for (var current = GetParent(window); current != IntPtr.Zero; current = GetParent(current))
        {
            if (current == root)
                return true;
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    private static IntPtr? AncestorOfClass(IntPtr hwnd, string className)
    {
        for (var current = hwnd; current != IntPtr.Zero; current = GetParent(current))
        {
            if (string.Equals(ClassOf(current), className, StringComparison.OrdinalIgnoreCase))
                return current;
        }
        return null;
    }

    private static string ClassOf(IntPtr hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "(unknown)";
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch
            {
                // Already released or not a COM object after all.
            }
        }
    }
}
