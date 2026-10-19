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

    /// <summary>Runs with a per-drop output folder (the folder the wheel was opened over).</summary>
    public void StartInto(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters, string? outputDir)
        => Start(op, items, parameters, outputDir);

    /// <param name="parameters">Values for the operation's questions; when given (repeat last) no dialog is shown.</param>
    public async void Start(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters)
        => Start(op, items, parameters, null);

    public async void Start(Operation op, IReadOnlyList<FileItem> items, IReadOnlyDictionary<string, string>? parameters, string? outputDir)
    {
        if (op is NeedsToolOperation needs)
        {
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
        var title = op.Wheel == WheelKind.Convert && op.Label.ToUpperInvariant() == op.Label ? T($"Převod na {op.Label}", $"Convert to {op.Label}") : op.Label;
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
        var context = new OperationContext
        {
            Tools = tools,
            Progress = new Progress<double>(card.Report),
            CancellationToken = cts.Token,
            OutputDirectory = outputDir ?? (string.IsNullOrWhiteSpace(settings.OutputDirectory) ? null : settings.OutputDirectory),
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
        if (ask)
        {
            var names = string.Join(" + ", missing.Select(t => Bidi.Value(ToolLocator.DisplayName(t))));
            var size = string.Join(" + ", missing.Select(t => Bidi.Value(ToolInstaller.DownloadSize(t))));
            var owner = ForegroundHelper.Host();
            var answer = Dialogs.Show(owner,
                T($"Pro tento převod je potřeba {names} (stažení {size}). Stáhnout teď? Stačí jednou.",
                  $"This conversion needs {names} ({size} download). Download it now? It's a one-time step."),
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
            var card = _window.Add(T($"Stahuji {toolName}", $"Downloading {toolName}"), toolSize, () =>
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
                card.Complete([], null, T("Připraveno, už se nebude stahovat znovu.", "Ready; it won't be downloaded again."));
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

    private void Remember(Operation op, IReadOnlyDictionary<string, string>? parameters)
    {
        settings.LastOperationId = op.Id;
        settings.LastParameters = parameters is { Count: > 0 } ? new Dictionary<string, string>(parameters) : null;
        settings.Save();
    }

    private static string Friendly(Exception ex) => ex switch
    {
        ConversionException => ex.Message,
        UnauthorizedAccessException => T("Do cílové složky nelze zapisovat. Zkuste v nastavení zvolit jinou výstupní složku.", "Cannot write to the target folder. Choose another output folder in Settings."),
        System.IO.IOException => T($"Chyba souboru: {ex.Message}", $"File error: {ex.Message}"),
        ImageMagick.MagickException => T($"Obrázek se nepodařilo zpracovat: {ex.Message}", $"Could not process the image: {ex.Message}"),
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
