using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.Linux;

/// <summary>An Actions-wheel entry that runs in the app rather than producing converted files.</summary>
public sealed class LinuxAction(string id, string label, string glyph, string? subtitle, Func<IReadOnlyList<FileItem>, Task> run, Func<IReadOnlyList<FileItem>, bool>? applies = null)
    : Operation(id, WheelKind.Actions, label, glyph, subtitle)
{
    public override bool AppliesTo(IReadOnlyList<FileItem> items) => items.Count > 0 && (applies?.Invoke(items) ?? true);

    public Task RunAsync(IReadOnlyList<FileItem> items) => run(items);

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        throw new NotSupportedException();
}

/// <summary>Copy, move, copy path, terminal and "show in folder" for the Actions wheel.</summary>
public static class LinuxActions
{
    public static IReadOnlyList<Operation> Build(Func<string, Func<CancellationToken, Task<IReadOnlyList<string>>>, Task> track) =>
    [
        new LinuxAction("action.copy", T("Kopírovat", "Copy"), Glyphs.Copy, T("do složky…", "to folder…"),
            items => CopyOrMoveAsync(items, move: false, track)),
        new LinuxAction("action.move", T("Přesunout", "Move"), Glyphs.Move, T("do složky…", "to folder…"),
            items => CopyOrMoveAsync(items, move: true, track)),
        new LinuxAction("action.path", T("Kopírovat cestu", "Copy path"), Glyphs.Link, null,
            items => WithHelperWindowAsync(async window =>
            {
                if (window.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, string.Join("\n", items.Select(i => i.Path)));
            })),
        new LinuxAction("action.terminal", T("Terminál", "Terminal"), Glyphs.Terminal, T("otevřít zde", "open here"), items =>
        {
            var folder = items[0].IsDirectory ? items[0].Path : Path.GetDirectoryName(items[0].Path)!;
            if (!Desktop.Terminal(folder))
                throw new ConversionException(T("Nenašel jsem terminál. Nastavte proměnnou TERMINAL.", "No terminal found. Set the TERMINAL variable."));
            return Task.CompletedTask;
        }),
        new LinuxAction("action.reveal", T("Ve složce", "Show"), Glyphs.Folder, T("ve správci souborů", "in file manager"), items =>
        {
            Desktop.Reveal(items[0].Path);
            return Task.CompletedTask;
        }),
    ];

    private static async Task CopyOrMoveAsync(IReadOnlyList<FileItem> items, bool move, Func<string, Func<CancellationToken, Task<IReadOnlyList<string>>>, Task> track)
    {
        string? target = null;
        await WithHelperWindowAsync(async window =>
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = move ? T("Kam přesunout?", "Move to…") : T("Kam zkopírovat?", "Copy to…") });
            target = folders.FirstOrDefault()?.TryGetLocalPath();
        });
        if (target is null)
            return;
        await track(move ? T("Přesouvám", "Moving") : T("Kopíruji", "Copying"), ct => Task.Run<IReadOnlyList<string>>(() =>
        {
            var results = new List<string>();
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                var source = item.Path.TrimEnd('/');
                var destination = item.IsDirectory
                    ? OutputPaths.UniqueDirectory(target, item.Name)
                    : OutputPaths.Unique(target, Path.GetFileNameWithoutExtension(item.Name), Path.GetExtension(item.Name).TrimStart('.'));
                if (item.IsDirectory)
                {
                    CopyDirectory(source, destination);
                    if (move)
                        Directory.Delete(source, recursive: true);
                }
                else if (move)
                {
                    File.Move(source, destination);
                }
                else
                {
                    File.Copy(source, destination);
                }
                results.Add(destination);
            }
            return results;
        }, ct));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    /// <summary>Folder pickers and the clipboard need an open window; the wheel may already be gone.</summary>
    private static async Task WithHelperWindowAsync(Func<Window, Task> action)
    {
        var window = new Window
        {
            Width = 1,
            Height = 1,
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Opacity = 0,
        };
        window.Show();
        try
        {
            await action(window);
        }
        finally
        {
            window.Close();
        }
    }
}
