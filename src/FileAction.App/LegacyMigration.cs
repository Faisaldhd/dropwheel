using System;
using System.IO;
using Microsoft.Win32;

namespace FileAction.App;

/// <summary>
/// One-time migration from the pre-rebrand FileAction layout (settings, downloaded tools,
/// the inbox folder and the autostart value) to the DropWheel layout.
/// Runs once at startup; every step is best-effort and never blocks the app.
/// </summary>
internal static class LegacyMigration
{
    private const string OldName = "FileAction";
    private const string NewName = "DropWheel";

    private static bool _done;

    public static void Run()
    {
        if (_done)
            return;
        _done = true;

        MoveFolder(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), NewName));

        MoveFolder(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), OldName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NewName));

        MoveFolder(Path.Combine(Downloads(), OldName), Path.Combine(Downloads(), NewName));

        MoveRunValue();
    }

    private static string Downloads()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        return Directory.Exists(downloads) ? downloads : profile;
    }

    private static void MoveFolder(string oldPath, string newPath)
    {
        try
        {
            if (!Directory.Exists(oldPath) || Directory.Exists(newPath))
                return;
            try
            {
                Directory.Move(oldPath, newPath);
            }
            catch (IOException)
            {
                // different volume or locked file: copy the tree, then leave the old one for the user
                CopyTree(oldPath, newPath);
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        catch
        {
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            try
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }
            catch
            {
            }
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            try
            {
                CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
            catch
            {
            }
        }
    }

    private static void MoveRunValue()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run == null)
                return;
            if (run.GetValue(OldName) is not string value || run.GetValue(NewName) != null)
                return;
            run.SetValue(NewName, value);
            run.DeleteValue(OldName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}
