using System.IO;
using System.Windows;
using FileAction.App.Diagnostics;
using FileAction.App.Actions;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.Jobs;

/// <summary>Runs an operation in the background and shows its progress card.</summary>
internal sealed class JobRunner(AppSettings settings, ToolLocator tools)
{
    private JobsWindow? _window;

    public void Start(Operation op, IReadOnlyList<FileItem> items) => Start(op, items, null);

    /// <summary>Runs with the folders detected for this drop (under the cursor, plus the remembered one).</summary>
    public void StartInto(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters, DropTargets? targets)
        => Start(op, items, parameters, targets);

    /// <param name="parameters">Values for the operation's questions; when given (repeat last) no dialog is shown.</param>
    public async void Start(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters)
        => Start(op, items, parameters, null);

    public async void Start(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters, DropTargets? targets)
    {
        if (op is NeedsToolOperation needs)
        {
            // LibreOffice is a backend DropWheel cannot download. Point at the official installer instead
            // of offering a download that would fail after the confirmation prompt.
            var separate = needs.Missing.Where(tool => !ToolInstaller.CanInstall(tool)).ToList();
            if (separate.Count > 0)
            {
                var names = string.Join(" + ", separate.Select(ToolLocator.DisplayName));
                var hints = string.Join("\n", separate.Select(ToolLocator.InstallHint));
                Dialogs.Show(
                    $"{names}\n\n{T("tool.installCommand")}\n{hints}",
                    "DropWheel", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (await DownloadToolsAsync(needs.Missing, ask: true))
                Start(needs.Inner, items, parameters);
            return;
        }

        if (op is AppAction action)
        {
            Remember(op, null);
            try
            {
                await action.RunAsync(items);
            }
            catch (Exception ex)
            {
                Notifications.Show(op.Label, ex.Message, error: true);
            }
            return;
        }

        parameters ??= new Dictionary<string, string>();
        if (op.Parameters.Count > 0 && parameters.Count == 0)
        {
            var dialog = new ParameterDialog(op, items, tools);
            if (dialog.ShowDialog() != true)
                return;
            parameters = dialog.Values;
        }
        Remember(op, parameters);

        _window ??= new JobsWindow();
        using var cts = new CancellationTokenSource();
        var title = op.Wheel == WheelKind.Convert && op.Label.ToUpperInvariant() == op.Label ? T("job.title.convertTo", op.Label) : op.Label;
        var detail = items.Count == 1 ? Bidi.Value(items[0].Name) : Items(items.Count);
        var card = _window.Add(title, detail, iconPath: items[0].Path, cancel: () =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        // A folder detected under the cursor receives this drop's results only when it still exists,
        // is writable and is not the folder the file already lives in. In a drag the cursor is usually
        // back over the source when the wheel opens, so the last folder the cursor crossed while the
        // drag was running is tried next. Anything else keeps the previous behaviour (next to the
        // source, or the folder chosen in Settings); a folder that exists but refuses a write says so.
        var sourcePath = items.Count > 0 ? items[0].Path : string.Empty;
        var chosen = OutputTarget.Resolve(sourcePath, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        var usedRemembered = false;
        if (!chosen.UsesDetectedFolder && !string.IsNullOrWhiteSpace(targets?.Remembered))
        {
            var remembered = OutputTarget.Resolve(sourcePath, targets!.Remembered, Directory.Exists, OutputTarget.CanWrite);
            if (remembered.UsesDetectedFolder)
            {
                chosen = remembered;
                usedRemembered = true;
            }
        }

        if (chosen.Status == OutputTargetStatus.NotWritable)
            Notifications.Show(T("app.error.title"), T("error.output.dropFolderFallback"), error: true);
        if (chosen.Directory is { } destination)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(destination));
            Notifications.Show(T("toast.dropFolder.title"), T("toast.dropFolder", Bidi.Value(string.IsNullOrEmpty(name) ? destination : name)));
        }

        ShellFolders.NoteDecision(
            $"source={(OutputTarget.SourceFolder(sourcePath) ?? "(none)")} " +
            $"cursor={(targets?.UnderCursor ?? "(none)")} remembered={(targets?.Remembered ?? "(none)")} " +
            $"-> {chosen.Status}{(usedRemembered ? " (remembered)" : string.Empty)} " +
            $"dir={(chosen.Directory ?? "(next to source)")}");

        var context = new OperationContext
        {
            Tools = tools,
            Progress = new Progress<double>(card.Report),
            CancellationToken = cts.Token,
            OutputDirectory = chosen.Directory ??
                (string.IsNullOrWhiteSpace(settings.OutputDirectory) ? null : settings.OutputDirectory),
            Parameters = parameters,
            Settings = settings.Quality,
        };

        try
        {
            var result = await Task.Run(() => op.ExecuteAsync(items, context));
            card.Complete(result.Outputs, result.Message, result.Note);
            if (settings.RevealResult && result.Outputs.Count > 0)
                JobCard.Reveal(result.Outputs[0]);
        }
        catch (OperationCanceledException)
        {
            card.Cancelled();
        }
        catch (Exception ex)
        {
            card.Fail(Friendly(ex));
        }
    }

    /// <summary>Downloads FFmpeg/Pandoc with a progress card; true when everything is installed.</summary>
    public async Task<bool> DownloadToolsAsync(IReadOnlyList<ExternalTool> missing, bool ask)
    {
        if (!Policy.DownloadsAllowed(settings))
        {
            // R-20: company-safe default. Never download unless the user (or IT) allows it.
            var blocked = string.Join(" + ", missing.Select(ToolLocator.DisplayName));
            AppLog.Info($"tool download blocked (auto-downloads off, policy={Policy.DownloadsDisabled})");
            Dialogs.Show(
                $"Automatic downloads are off, so {blocked} cannot be downloaded.\n\n" +
                $"Install it yourself (for example: {ToolLocator.InstallHint(missing[0])}) and DropWheel will find it, " +
                "or enable downloads in Settings > Company mode.",
                "DropWheel", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (ask)
        {
            var names = string.Join(" + ", missing.Select(t => Bidi.Value(ToolLocator.DisplayName(t))));
            var size = string.Join(" + ", missing.Select(t => Bidi.Value(ToolInstaller.DownloadSize(t))));
            var owner = ForegroundHelper.Host();
            var answer = Dialogs.Show(owner,
                T("job.download.prompt", names, size),
                "DropWheel", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            owner.Close();
            if (answer != System.Windows.MessageBoxResult.Yes)
                return false;
        }

        _window ??= new JobsWindow();
        foreach (var tool in missing)
        {
            using var cts = new CancellationTokenSource();
            var toolName = Bidi.Value(ToolLocator.DisplayName(tool));
            var toolSize = Bidi.Value(ToolInstaller.DownloadSize(tool));
            var card = _window.Add(T("job.download.downloading", toolName), toolSize, () =>
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            });
            try
            {
                await ToolInstaller.InstallAsync(tool, new Progress<double>(card.Report), cts.Token);
                tools.Refresh();
                card.Complete([], null, T("job.download.ready"));
            }
            catch (OperationCanceledException)
            {
                card.Cancelled();
                return false;
            }
            catch (ToolInstallException ex)
            {
                // R-16: name the tool, the folder and the OS error; never blame the output folder here.
                AppLog.Error($"tool install failed: {ex.Tool} into {ex.Path}", ex.InnerException ?? ex);
                card.Fail(ex.Describe());
                return false;
            }
            catch (Exception ex)
            {
                card.Fail(Friendly(ex));
                return false;
            }
        }
        return true;
    }

    internal void Remember(Operation op, IReadOnlyDictionary<string, string>? parameters, bool save = true)
    {
        settings.LastOperationId = op.Id;
        settings.LastParameters = parameters is { Count: > 0 } ? new Dictionary<string, string>(parameters) : null;
        if (save)
            settings.Save();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        ConversionException => ex.Message,
        UnauthorizedAccessException => T("error.output.notWritable"),
        System.IO.IOException => T("error.file", ex.Message),
        ImageMagick.MagickException => T("error.image.process", ex.Message),
        // A hostile path (illegal characters, an unsupported path form) is still a file problem the user
        // can act on; it must never reach the card as "ArgumentException: Illegal characters in path.".
        ArgumentException or NotSupportedException => T("error.file", ex.Message),
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
