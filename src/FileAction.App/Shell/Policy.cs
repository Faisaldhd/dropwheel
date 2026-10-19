using System.IO;
using Microsoft.Win32;

namespace FileAction.App.Shell;

/// <summary>
/// R-20: machine-wide switches an IT department can set, so the app can be approved for a company PC.
/// <c>HKLM\SOFTWARE\Policies\DropWheel\DisableHooks=1</c> → no global keyboard/mouse hooks at all;
/// <c>DisableDownloads=1</c> → the app never downloads helper tools.
/// </summary>
internal static class Policy
{
    private const string Path = @"SOFTWARE\Policies\DropWheel";

    /// <summary>True when IT disabled global input hooks for this machine.</summary>
    public static bool HooksDisabled => Read("DisableHooks");

    /// <summary>True when IT disabled helper-tool downloads for this machine.</summary>
    public static bool DownloadsDisabled => Read("DisableDownloads");

    /// <summary>True when the app may install keyboard/mouse hooks (setting, policy and the --no-hooks flag).</summary>
    public static bool HooksAllowed(AppSettings settings, string[] args) =>
        settings.WatchGestures && !HooksDisabled && !args.Contains("--no-hooks");

    /// <summary>True when the app may download FFmpeg/Pandoc (setting and policy).</summary>
    public static bool DownloadsAllowed(AppSettings settings) =>
        settings.AutoDownloadTools && !DownloadsDisabled;

    /// <summary>
    /// Interprets one policy value: <c>1</c>/<c>true</c>/<c>yes</c> (any nonzero number) enables, anything
    /// else disables. Kept separate from <see cref="Read"/> so the rules can be tested without touching HKLM.
    /// </summary>
    internal static bool ParsePolicyValue(object? value) => value switch
    {
        int number => number != 0,
        long number => number != 0,
        string text => text.Trim().ToLowerInvariant() is "1" or "true" or "yes",
        _ => false,
    };

    private static bool Read(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Path);
            return ParsePolicyValue(key?.GetValue(name));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
