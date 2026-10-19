using System.Diagnostics;
using System.IO;
using System.Windows;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using Microsoft.VisualBasic.FileIO;
using static FileAction.Core.Loc;

namespace FileAction.App.Actions;

/// <summary>The Actions wheel: copy, move, copy path, terminal, floating shelf and share.</summary>
internal static class FileActions
{
    public static IReadOnlyList<Operation> Build(AppSettings settings, Action save) =>
    [
        new AppAction("action.copy", T("action.copy.label"), Glyphs.Copy, items => CopyOrMoveAsync(items, move: false, settings, save), T("action.toFolder.subtitle")),
        new AppAction("action.move", T("action.move.label"), Glyphs.Move, items => CopyOrMoveAsync(items, move: true, settings, save), T("action.toFolder.subtitle")),
        new AppAction("action.path", T("action.copyPath.label"), Glyphs.Link, CopyPathAsync),
        new AppAction("action.terminal", T("action.terminal.label"), Glyphs.Terminal, OpenTerminalAsync, T("action.terminal.subtitle")),
        new AppAction("action.float", T("action.float.label"), Glyphs.Pin, items =>
        {
            ShelfWindow.Open(items);
            return Task.CompletedTask;
        }, T("action.float.subtitle")),
        new AppAction("action.share", T("action.share.label"), Glyphs.Share, ShareHelper.ShareAsync, "Windows",
            items => items.Count > 0 && items.All(i => !i.IsDirectory)),
    ];

    private static async Task CopyOrMoveAsync(IReadOnlyList<FileItem> items, bool move, AppSettings settings, Action save)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = move ? T("action.move.dialogTitle") : T("action.copy.dialogTitle"),
            InitialDirectory = (move ? settings.LastMoveFolder : settings.LastCopyFolder)
                ?? Path.GetDirectoryName(items[0].Path.TrimEnd('\\', '/')) ?? "",
        };
        // We are a background app at this point; a foreground owner keeps the dialog from opening behind Explorer.
        var owner = ForegroundHelper.Host();
        try
        {
            if (dialog.ShowDialog(owner) != true)
                return;
        }
        finally
        {
            owner.Close();
        }
        var target = dialog.FolderName;
        if (move)
            settings.LastMoveFolder = target;
        else
            settings.LastCopyFolder = target;
        save();

        // The shell dialogs give the familiar progress window, conflict prompts and undo.
        await RunOnStaThread(() =>
        {
            foreach (var item in items)
            {
                var source = item.Path.TrimEnd('\\', '/');
                var destination = Path.Combine(target, item.Name);
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.IsDirectory)
                {
                    if (move)
                        FileSystem.MoveDirectory(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                    else
                        FileSystem.CopyDirectory(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else if (move)
                {
                    FileSystem.MoveFile(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
                else
                {
                    FileSystem.CopyFile(source, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
                }
            }
        });
    }

    private static Task RunOnStaThread(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    private static Task CopyPathAsync(IReadOnlyList<FileItem> items)
    {
        var text = string.Join(Environment.NewLine, items.Select(i => i.Path));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                break;
            }
            catch (System.Runtime.InteropServices.COMException) when (attempt < 5)
            {
                // Another app holds the clipboard open; try again shortly.
                Thread.Sleep(50);
            }
        }
        Notifications.Show(T("action.pathCopied"), items.Count == 1 ? Bidi.Value(items[0].Path) : Items(items.Count));
        return Task.CompletedTask;
    }

    private static Task OpenTerminalAsync(IReadOnlyList<FileItem> items)
    {
        var first = items[0];
        var folder = first.IsDirectory ? first.Path : Path.GetDirectoryName(first.Path)!;
        try
        {
            // Windows Terminal, if installed (app execution alias).
            Process.Start(new ProcessStartInfo("wt.exe", $"-d {QuotePath(folder)}") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit") { WorkingDirectory = folder, UseShellExecute = true });
        }
        return Task.CompletedTask;
    }

    /// <summary>Quotes a folder for a command line; "C:\" would otherwise escape the closing quote.</summary>
    private static string QuotePath(string folder) =>
        "\"" + (folder.EndsWith('\\') ? folder + "." : folder) + "\"";
}
