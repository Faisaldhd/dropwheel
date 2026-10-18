using Avalonia.Controls.ApplicationLifetimes;
using FileAction.Core;
using FileAction.Core.Operations;
using FileAction.Linux.UI;
using static FileAction.Core.Loc;

namespace FileAction.Linux;

/// <summary>Builds the wheels, runs operations with progress cards and quits when nothing is left on screen.</summary>
public sealed class Controller
{
    private readonly LinuxSettings _settings;
    private readonly ToolLocator _tools = new();
    private readonly IReadOnlyList<Operation> _conversions;
    private readonly IReadOnlyList<Operation> _actions;
    private readonly IClassicDesktopStyleApplicationLifetime? _lifetime;
    private JobsWindow? _jobs;
    private int _running;
    private bool _wheelOpen;

    public Controller(LinuxSettings settings, IClassicDesktopStyleApplicationLifetime? lifetime)
    {
        _settings = settings;
        _lifetime = lifetime;
        _conversions = LinuxOperations.Conversions(settings);
        _actions = LinuxActions.Build(TrackAsync);
    }

    public IReadOnlyList<Operation> Operations(WheelKind wheel, IReadOnlyList<FileItem> items) =>
        WheelLayout.Arrange(Catalog.For(wheel == WheelKind.Actions ? _actions : _conversions, wheel, items, _tools),
            _settings.HiddenOperations, _settings.FavoriteOperations);

    public Operation? LastFor(IReadOnlyList<FileItem> items)
    {
        var op = _settings.LastOperationId is { } id ? _conversions.Concat(_actions).FirstOrDefault(o => o.Id == id) : null;
        return op is not null && op.AppliesTo(items) && op.IsAvailable(_tools, items) ? op : null;
    }

    public WheelWindow CreateWheel(IReadOnlyList<FileItem> items)
    {
        var wheel = new WheelWindow(items, Operations, (op, targets, repeat) => _ = ExecuteAsync(op, targets, repeat)) { LastOperation = LastFor };
        _wheelOpen = true;
        wheel.Closed += (_, _) =>
        {
            _wheelOpen = false;
            ExitIfIdle();
        };
        return wheel;
    }

    private async Task ExecuteAsync(Operation op, IReadOnlyList<FileItem> items, bool repeat)
    {
        _running++;
        try
        {
            if (op is LinuxAction action)
            {
                Remember(op, null);
                try
                {
                    await action.RunAsync(items);
                }
                catch (Exception ex)
                {
                    ShowError(op.Label, ex.Message);
                }
                return;
            }

            IReadOnlyDictionary<string, string>? parameters = repeat ? _settings.LastParameters : null;
            if (op.Parameters.Count > 0 && parameters is not { Count: > 0 })
            {
                var dialog = new ParameterWindow(op, items);
                if (!await dialog.AskAsync())
                    return;
                parameters = dialog.Values;
            }
            Remember(op, parameters);

            var title = op.Wheel == WheelKind.Convert && op.Label.ToUpperInvariant() == op.Label ? T($"Převod na {op.Label}", $"Convert to {op.Label}") : op.Label;
            await TrackAsync(title, async ct =>
            {
                var context = new OperationContext
                {
                    Tools = _tools,
                    Parameters = parameters ?? new Dictionary<string, string>(),
                    OutputDirectory = string.IsNullOrWhiteSpace(_settings.OutputDirectory) ? null : _settings.OutputDirectory,
                    Settings = _settings.Quality,
                    CancellationToken = ct,
                    Progress = CurrentProgress,
                };
                var result = await Task.Run(() => op.ExecuteAsync(items, context), ct);
                _lastNote = result.Note;
                _lastWarning = result.Message;
                return result.Outputs;
            }, items.Count == 1 ? items[0].Name : Items(items.Count));
        }
        finally
        {
            _running--;
            ExitIfIdle();
        }
    }

    private IProgress<double> CurrentProgress = new Progress<double>();
    private string? _lastNote;
    private string? _lastWarning;

    /// <summary>Runs work with a progress card.</summary>
    public async Task TrackAsync(string title, Func<CancellationToken, Task<IReadOnlyList<string>>> work) =>
        await TrackAsync(title, work, "");

    private async Task TrackAsync(string title, Func<CancellationToken, Task<IReadOnlyList<string>>> work, string detail)
    {
        _jobs ??= CreateJobsWindow();
        using var cts = new CancellationTokenSource();
        var card = _jobs.Add(title, detail, () =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        });
        _running++;
        try
        {
            CurrentProgress = new Progress<double>(card.Report);
            _lastNote = null;
            _lastWarning = null;
            var outputs = await work(cts.Token);
            card.Complete(outputs, _lastWarning, _lastNote);
        }
        catch (OperationCanceledException)
        {
            card.Cancelled();
        }
        catch (Exception ex)
        {
            card.Fail(ex is ConversionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _running--;
        }
    }

    private JobsWindow CreateJobsWindow()
    {
        var window = new JobsWindow();
        window.Emptied += ExitIfIdle;
        return window;
    }

    private void ShowError(string title, string message)
    {
        _jobs ??= CreateJobsWindow();
        _jobs.Add(title, "", () => { }).Fail(message);
    }

    private void Remember(Operation op, IReadOnlyDictionary<string, string>? parameters)
    {
        _settings.LastOperationId = op.Id;
        _settings.LastParameters = parameters is { Count: > 0 } ? new Dictionary<string, string>(parameters) : null;
        _settings.Save();
    }

    /// <summary>Quit once the wheel is closed, nothing runs and no progress card is left.</summary>
    private void ExitIfIdle()
    {
        if (_wheelOpen || _running > 0 || (_jobs is not null && _jobs.Cards.Children.Count > 0))
            return;
        _lifetime?.Shutdown();
    }
}
