using System.Diagnostics;

namespace FileAction.Linux;

/// <summary>Opening files and folders through the desktop (xdg-open, the FileManager1 D-Bus interface).</summary>
public static class Desktop
{
    public static void Open(string path) => Start("xdg-open", path);

    /// <summary>Shows a file selected in the file manager, or opens its folder when that isn't supported.</summary>
    public static void Reveal(string path)
    {
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
        if (!Start("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", $"array:string:{uri}", "string:"))
            Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
    }

    /// <summary>A terminal in <paramref name="folder"/>: $TERMINAL or the first common one found.</summary>
    public static bool Terminal(string folder)
    {
        var candidates = new[] { Environment.GetEnvironmentVariable("TERMINAL"), "x-terminal-emulator", "kgx", "ptyxis", "gnome-terminal", "konsole",
            "xfce4-terminal", "alacritty", "kitty", "foot", "wezterm", "tilix", "xterm" };
        foreach (var name in candidates.Where(c => !string.IsNullOrEmpty(c)))
        {
            if (TesseractOcr.FindInPath(name!) is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = false });
                return true;
            }
        }
        return false;
    }

    private static bool Start(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            if (!p.WaitForExit(3000))
                return true; // still running (e.g. xdg-open handing over): assume fine
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
