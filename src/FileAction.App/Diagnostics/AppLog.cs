using System.Diagnostics;
using System.IO;
using System.Text;

namespace FileAction.App.Diagnostics;

/// <summary>
/// R-18: the app must always leave a trace. On a locked-down machine <c>%APPDATA%\DropWheel</c> could not be
/// created, nothing was logged and the silent exit could not be diagnosed. This log falls back through
/// <c>%APPDATA%</c> → <c>%LOCALAPPDATA%</c> → <c>%TEMP%</c> → the folder next to the exe.
/// </summary>
internal static class AppLog
{
    private static readonly object Gate = new();
    private static string? _file;

    /// <summary>The log file that is actually in use, or null when every location was refused.</summary>
    public static string? FilePath => _file;

    /// <summary>The folder of the active log, or an empty string when logging is unavailable.</summary>
    public static string Folder => _file is null ? "" : Path.GetDirectoryName(_file) ?? "";

    /// <summary>Candidate folders, most desirable first.</summary>
    public static IEnumerable<string> CandidateFolders()
    {
        yield return AppSettings.Folder;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel");
        yield return Path.Combine(Path.GetTempPath(), "DropWheel");
        yield return AppContext.BaseDirectory;
    }

    /// <summary>Picks the first writable folder. Never throws.</summary>
    public static void Initialize()
    {
        foreach (var folder in CandidateFolders())
        {
            try
            {
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "log.txt");
                using (new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                }
                _file = file;
                var version = typeof(AppLog).Assembly.GetName().Version?.ToString() ?? "?";
                Info($"--- DropWheel {version} starting on {Environment.OSVersion} (.NET {Environment.Version}, " +
                     $"{(Environment.Is64BitProcess ? "x64" : "x86")}, integrity {Integrity.Describe()}) ---");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // Try the next location.
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null
            ? message
            : $"{message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    /// <summary>The last <paramref name="lines"/> lines of the log, for crash reports and --diagnose.</summary>
    public static string Tail(int lines = 40)
    {
        try
        {
            if (_file is null || !File.Exists(_file))
                return "(no log)";
            var all = File.ReadAllLines(_file);
            return string.Join(Environment.NewLine, all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(log unreadable: " + ex.Message + ")";
        }
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {message}";
        Debug.WriteLine(line);
        lock (Gate)
        {
            if (_file is null)
                return;
            try
            {
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
