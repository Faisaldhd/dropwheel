using System.IO;
using System.Text;
using System.Windows;

namespace FileAction.App.Diagnostics;

/// <summary>
/// R-18: a crash on a locked-down machine used to look like "the app opened Settings and vanished". Every
/// unhandled exception now writes a report and says what happened, instead of exiting silently.
/// </summary>
internal static class CrashGuard
{
    private static int _reporting;

    public static void Install(Application app)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("AppDomain", e.ExceptionObject as Exception);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Report("Dispatcher", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report("Task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes a crash report and tells the user. Returns the report text (empty when re-entered).</summary>
    public static string Report(string source, Exception? exception)
    {
        if (Interlocked.Exchange(ref _reporting, 1) == 1)
            return string.Empty;
        try
        {
            var text = BuildReport(source, exception);
            var path = WriteReport(text);
            AppLog.Error($"unhandled {source} exception, report saved to {path}", exception);
            TryCopyToClipboard(text);
            ShowDialog(exception, path);
            return text;
        }
        finally
        {
            Interlocked.Exchange(ref _reporting, 0);
        }
    }

    private static string BuildReport(string source, Exception? exception)
    {
        var text = new StringBuilder();
        text.AppendLine($"DropWheel crash report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"source:     {source}");
        text.AppendLine($"version:    {typeof(CrashGuard).Assembly.GetName().Version}");
        text.AppendLine($"os:         {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
        text.AppendLine($"integrity:  {Integrity.Describe()}");
        text.AppendLine($"exe:        {Environment.ProcessPath}");
        text.AppendLine($"log:        {AppLog.FilePath ?? "(none)"}");
        text.AppendLine();
        if (exception is not null)
        {
            text.AppendLine($"{exception.GetType().FullName}: {exception.Message}");
            text.AppendLine(exception.StackTrace);
            if (exception.InnerException is not null)
            {
                text.AppendLine();
                text.AppendLine($"inner: {exception.InnerException.GetType().FullName}: {exception.InnerException.Message}");
                text.AppendLine(exception.InnerException.StackTrace);
            }
        }
        else
        {
            text.AppendLine("(no exception object)");
        }
        text.AppendLine();
        text.AppendLine("--- log tail ---");
        text.AppendLine(AppLog.Tail(80));
        return text.ToString();
    }

    private static string WriteReport(string text)
    {
        var folder = string.IsNullOrEmpty(AppLog.Folder) ? Path.GetTempPath() : AppLog.Folder;
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(report could not be written: " + ex.Message + ")";
        }
    }

    private static void TryCopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Clipboard can be busy or unavailable; the file on disk is the primary record.
        }
    }

    private static void ShowDialog(Exception? exception, string path)
    {
        var reason = exception is null ? "unknown error" : $"{exception.GetType().Name}: {exception.Message}";
        var message = $"DropWheel stopped: {reason}\n\nThe details were copied to the clipboard and saved to:\n{path}\n\n" +
                      "If this keeps happening, run \"DropWheel.exe --diagnose\" and send the report.";
        try
        {
            var app = Application.Current;
            if (app is not null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(() => MessageBox.Show(message, "DropWheel", MessageBoxButton.OK, MessageBoxImage.Error));
            else
                MessageBox.Show(message, "DropWheel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // No UI available (service/headless); the log and the report file remain.
        }
    }
}
