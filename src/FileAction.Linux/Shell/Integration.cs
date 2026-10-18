using static FileAction.Core.Loc;

namespace FileAction.Linux.Shell;

/// <summary>
/// "DropWheel…" in the right-click menu of Nautilus (Files), Dolphin and Nemo, for the current user.
/// The Arch package installs the Dolphin and Nemo entries system-wide; Nautilus only reads per-user scripts.
/// </summary>
public static class Integration
{
    private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
        ? xdg
        : Path.Combine(LinuxSettings.Home, ".local", "share");

    /// <summary>"dropwheel" when it is on PATH (package install), otherwise the full path of this binary.</summary>
    private static string Command =>
        TesseractOcr.FindInPath("dropwheel") is not null ? "dropwheel" : Environment.ProcessPath ?? "dropwheel";

    public static string NautilusScript(string command) => $"""
        #!/bin/sh
        # Added by "dropwheel integrate".
        IFS='
        '
        exec {command} $NAUTILUS_SCRIPT_SELECTED_FILE_PATHS

        """;

    public static string DolphinServiceMenu(string command) => $"""
        [Desktop Entry]
        Type=Service
        MimeType=all/all;
        Actions=dropwheel
        X-KDE-Priority=TopLevel

        [Desktop Action dropwheel]
        Name=DropWheel…
        Icon=dropwheel
        Exec={command} %F

        """;

    public static string NemoAction(string command) => $"""
        [Nemo Action]
        Name=DropWheel…
        Comment=Convert or process with DropWheel
        Exec={command} %F
        Icon-Name=dropwheel
        Selection=notnone
        Extensions=any;

        """;

    /// <summary>The desktop launcher installed by packages.</summary>
    public static string DesktopEntry(string command) => $"""
        [Desktop Entry]
        Type=Application
        Name=DropWheel
        GenericName=File converter
        Comment=Convert files by dropping them on a wheel
        Comment[cs]=Převod souborů puštěním na kolo
        Exec={command} %F
        Icon=dropwheel
        Terminal=false
        Categories=Utility;FileTools;
        Keywords=convert;converter;image;video;audio;pdf;ocr;

        """;

    /// <summary>Writes the files a package installs system-wide (used by packaging).</summary>
    public static IEnumerable<string> Export(string dir)
    {
        Directory.CreateDirectory(dir);
        var files = new (string Name, string Content)[]
        {
            ("dropwheel.desktop", DesktopEntry("dropwheel")),
            ("dolphin-servicemenu.desktop", DolphinServiceMenu("dropwheel")),
            ("dropwheel.nemo_action", NemoAction("dropwheel")),
        };
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
            yield return Path.Combine(dir, name);
        }
    }

    public static IEnumerable<string> Apply(bool install)
    {
        var command = Command;
        var targets = new (string Path, string Content, bool Executable, string Name)[]
        {
            (Path.Combine(DataHome, "nautilus", "scripts", "DropWheel"), NautilusScript(command), true, "Nautilus (Files)"),
            (Path.Combine(DataHome, "kio", "servicemenus", "dropwheel.desktop"), DolphinServiceMenu(command), true, "Dolphin"),
            (Path.Combine(DataHome, "nemo", "actions", "dropwheel.nemo_action"), NemoAction(command), false, "Nemo"),
        };
        foreach (var (path, content, executable, name) in targets)
        {
            if (!install)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    yield return T($"Odebráno: {name}", $"Removed: {name}");
                }
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            if (executable)
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            yield return T($"Přidáno: {name} → {path}", $"Added: {name} → {path}");
        }
        if (install)
        {
            yield return T("V Nautilu je položka v nabídce Skripty → DropWheel. Thunar: Upravit → Vlastní akce, příkaz: ",
                "In Nautilus the item is under Scripts → DropWheel. Thunar: Edit → Configure custom actions, command: ") + $"{command} %F";
        }
    }
}
