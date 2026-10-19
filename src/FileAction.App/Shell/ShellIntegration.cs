using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell;

/// <summary>Autostart, the "DropWheel…" Explorer context-menu entry and the opt-in "Send to" shortcut (per user, no admin needed).</summary>
internal static class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DropWheel";
    private static readonly string[] MenuRoots = [@"Software\Classes\*\shell\DropWheel", @"Software\Classes\Directory\shell\DropWheel"];

    private static string Exe => Environment.ProcessPath ?? throw new InvalidOperationException(T("Neznámá cesta k aplikaci.", "Unknown application path."));

    public static bool IsAutostartEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && value.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Exe}\" --background");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static bool IsContextMenuEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuRoots[0] + @"\command");
            return key?.GetValue(null) is string value && value.Contains(Exe, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void SetContextMenu(bool enabled)
    {
        foreach (var root in MenuRoots)
        {
            if (!enabled)
            {
                Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
                continue;
            }
            using var key = Registry.CurrentUser.CreateSubKey(root);
            key.SetValue(null, "DropWheel…");
            key.SetValue("Icon", $"\"{Exe}\",0");
            key.SetValue("MultiSelectModel", "Player");
            using var command = key.CreateSubKey("command");
            command.SetValue(null, $"\"{Exe}\" --wheel \"%1\"");
        }
    }

    /// <summary>Re-points registered entries at the current exe after the app was moved or updated.</summary>
    public static void Refresh()
    {
        try
        {
            if (Registry.CurrentUser.OpenSubKey(MenuRoots[0]) is { } existing)
            {
                existing.Dispose();
                SetContextMenu(true);
            }
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not null)
                SetAutostart(true);
            if (File.Exists(SendToShortcut))
                SetSendTo(true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or COMException or InvalidOperationException)
        {
        }
    }

    private static string SendToFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "SendTo");

    private static string SendToShortcut => Path.Combine(SendToFolder, "DropWheel.lnk");

    /// <summary>True when the opt-in "Send to" shortcut exists; nothing is created until the user asks for it.</summary>
    public static bool IsSendToEnabled => File.Exists(SendToShortcut);

    /// <summary>
    /// R-20: adds or removes <c>DropWheel.lnk</c> in <c>%APPDATA%\Microsoft\Windows\SendTo</c>. The shortcut
    /// starts <c>DropWheel.exe --wheel</c>; Explorer appends the selected files to that command line.
    /// </summary>
    public static void SetSendTo(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(SendToShortcut))
                File.Delete(SendToShortcut);
            return;
        }

        Directory.CreateDirectory(SendToFolder);
        WriteSendToShortcut();
    }

    /// <summary>
    /// Writes the shortcut through the shell's own <c>IShellLink</c>/<c>IPersistFile</c> interfaces. Windows
    /// Script Host is deliberately not used: managed PCs often disable it by policy.
    /// </summary>
    private static void WriteSendToShortcut()
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            link.SetPath(Exe);
            link.SetArguments("--wheel");
            link.SetWorkingDirectory(Path.GetDirectoryName(Exe) ?? AppContext.BaseDirectory);
            link.SetDescription("DropWheel");
            link.SetIconLocation(Exe, 0);
            ((IPersistFile)link).Save(SendToShortcut, true);
        }
        finally
        {
            if (link is not null)
                Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    /// <summary>IShellLinkW up to SetPath; the unused members keep the COM vtable order intact.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    /// <summary>IPersistFile is what actually writes (and overwrites) the .lnk file.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
