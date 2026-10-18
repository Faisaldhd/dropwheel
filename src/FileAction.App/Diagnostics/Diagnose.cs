using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FileAction.App.Native;
using FileAction.Core;

namespace FileAction.App.Diagnostics;

/// <summary>
/// R-18: <c>DropWheel.exe --diagnose</c> writes everything needed to explain a machine where the app cannot
/// write, cannot hook the mouse/keyboard or cannot find a tool, then opens the report.
/// </summary>
internal static class Diagnose
{
    public static int Run(string? target = null)
    {
        string path;
        try
        {
            path = string.IsNullOrWhiteSpace(target) ? DefaultPath() : Path.GetFullPath(target);
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, Build(), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("--diagnose could not write its report", ex);
            return 2;
        }

        AppLog.Info($"--diagnose report written to {path}");
        TryOpen(path);
        return 0;
    }

    private static string DefaultPath()
    {
        var folder = string.IsNullOrEmpty(AppLog.Folder) ? Path.GetTempPath() : AppLog.Folder;
        return Path.Combine(folder, $"diagnose-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    }

    private static string Build()
    {
        var text = new StringBuilder();
        var version = typeof(Diagnose).Assembly.GetName().Version?.ToString() ?? "?";
        text.AppendLine("DropWheel diagnostics");
        text.AppendLine("====================");
        text.AppendLine($"generated:  {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"version:    {version}");
        text.AppendLine($"os:         {Environment.OSVersion}");
        text.AppendLine($"process:    {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}, pid {Environment.ProcessId}");
        text.AppendLine($"exe:        {Environment.ProcessPath}");
        text.AppendLine($"integrity:  {Integrity.Describe()}{(Integrity.IsLow ? "  <-- PROBLEM: low integrity cannot write to %APPDATA%/%TEMP% or receive input" : "")}");
        text.AppendLine($"culture:    {CultureInfo.CurrentUICulture.Name} (system {CultureInfo.InstalledUICulture.Name})");
        text.AppendLine($"language:   Loc.Current={Loc.Current}, Loc.Preferred={Loc.Preferred}");
        text.AppendLine();

        text.AppendLine("Folders (first writable one holds the log)");
        text.AppendLine("-----------------------------------------");
        foreach (var folder in AppLog.CandidateFolders())
            text.AppendLine($"  {(Writable(folder) ? "writable  " : "BLOCKED   ")} {folder}");
        text.AppendLine($"  active log: {AppLog.FilePath ?? "(none — every folder was refused)"}");
        var settings = Path.Combine(AppSettings.Folder, "settings.json");
        text.AppendLine($"  settings:   {settings} ({(File.Exists(settings) ? new FileInfo(settings).Length + " bytes" : "missing")})");
        text.AppendLine();

        text.AppendLine("Input hooks");
        text.AppendLine("-----------");
        var (ok, error) = ProbeHooks();
        text.AppendLine(ok
            ? "  WH_KEYBOARD_LL installed and removed again: OK"
            : $"  WH_KEYBOARD_LL FAILED, Win32 error {error}: {new System.ComponentModel.Win32Exception(error).Message}");
        text.AppendLine("  (the app installs keyboard and mouse hooks on a dedicated thread at startup)");
        text.AppendLine();

        text.AppendLine("Helper tools");
        text.AppendLine("------------");
        var tools = new ToolLocator();
        foreach (var tool in new[] { ExternalTool.Ffmpeg, ExternalTool.Pandoc, ExternalTool.LibreOffice })
            text.AppendLine($"  {ToolLocator.DisplayName(tool),-14} {(tools.Find(tool) ?? "not found")}");
        foreach (var ownTool in new[] { "winget", "dotnet" })
            text.AppendLine($"  {ownTool,-14} {FindOnPath(ownTool) ?? "not found"}");
        text.AppendLine();

        text.AppendLine("Environment");
        text.AppendLine("-----------");
        foreach (var name in new[] { "APPDATA", "LOCALAPPDATA", "TEMP", "TMP", "USERPROFILE", "ProgramData", "DOTNET_ROOT" })
            text.AppendLine($"  {name,-14} {Environment.GetEnvironmentVariable(name) ?? "(unset)"}");
        text.AppendLine();

        text.AppendLine("Log tail");
        text.AppendLine("--------");
        text.AppendLine(AppLog.Tail(80));
        return text.ToString();
    }

    private static bool Writable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".write-probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static (bool Ok, int Error) ProbeHooks()
    {
        var module = Win32.GetModuleHandle(null);
        Win32.HookProc procedure = static (nCode, wParam, lParam) => Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        var hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, procedure, module, 0);
        var error = Marshal.GetLastWin32Error();
        if (hook != IntPtr.Zero)
            Win32.UnhookWindowsHookEx(hook);
        else if (error == 0)
            error = 1; // SetWindowsHookEx returned null without setting an error; do not report "success".
        return (hook != IntPtr.Zero, error);
    }

    private static string? FindOnPath(string name)
    {
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), name + extension.ToLowerInvariant());
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Malformed PATH entry.
                }
            }
        }
        return null;
    }

    private static void TryOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // The report path is printed by the caller; opening it is best effort.
        }
    }
}
