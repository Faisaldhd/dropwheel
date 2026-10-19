using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FileAction.App.Diagnostics;

namespace FileAction.App.Updates;

/// <summary>
/// Starts the verified Inno Setup installer for an installed (non-portable) build. The setup runs silently and
/// closes the running app; the app then asks the dispatcher to exit.
/// </summary>
internal static class SetupUpdater
{
    private static readonly string[] SilentArguments =
        ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS"];

    /// <summary>Starts the setup. Returns null on success, or a clear error for the UI.</summary>
    public static string? Start(string setupPath)
    {
        try
        {
            if (!File.Exists(setupPath))
                return "The downloaded setup file is missing.";

            var info = new ProcessStartInfo(setupPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? AppContext.BaseDirectory,
            };
            foreach (var argument in SilentArguments)
                info.ArgumentList.Add(argument);
            info.ArgumentList.Add(InstallModeArgument());

            using var process = Process.Start(info);
            if (process is null)
                return "The setup could not be started.";

            AppLog.Info(
                $"update apply: started the verified setup (pid {process.Id}) with {string.Join(' ', info.ArgumentList)}");
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException
                                       or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            AppLog.Error("update apply: could not start the setup", ex);
            return "The setup could not be started: " + ex.Message;
        }
    }

    /// <summary>The installer scope: a per-user install stays per-user, anything else keeps all-users.</summary>
    internal static string InstallModeArgument() => IsCurrentUserInstall() ? "/CURRENTUSER" : "/ALLUSERS";

    /// <summary>True when the app runs from the default per-user location, %LOCALAPPDATA%\Programs\….</summary>
    internal static bool IsCurrentUserInstall()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            return false;

        var programs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(localAppData, "Programs")));
        var app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        return app.Equals(programs, StringComparison.OrdinalIgnoreCase)
            || app.StartsWith(programs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
