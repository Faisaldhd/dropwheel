using System.IO;
using System.Windows.Threading;
using FileAction.App.Actions;
using FileAction.App.Input;
using FileAction.App.Jobs;
using FileAction.App.Native;
using FileAction.App.Shell;
using FileAction.Core;
using FileAction.Core.Operations;
using static FileAction.Core.Loc;

namespace FileAction.App.Wheel;

/// <summary>Decides when a wheel opens (drag gesture, hotkey, context menu) and with which operations.</summary>
internal sealed class WheelController
{
    private readonly AppSettings _settings;
    private readonly ToolLocator _tools;
    private readonly JobRunner _jobs;
    private readonly GlobalInput _input;
    private IReadOnlyList<Operation> _coreOperations = BuildOperations();
    private IReadOnlyList<Operation> _actions;
    private readonly List<string> _pendingPaths = [];
    private readonly DispatcherTimer _argsTimer;
    private readonly DropFolderMemory _dragMemory = new();
    private readonly DispatcherTimer _dragTimer;
    private string? _dragOriginFolder;
    private string? _wheelSourceFolder;
    private WheelWindow? _wheel;

    public WheelController(AppSettings settings, ToolLocator tools, JobRunner jobs, GlobalInput input)
    {
        _settings = settings;
        _tools = tools;
        _jobs = jobs;
        _input = input;
        _actions = FileActions.Build(settings, settings.Save);

        // Explorer starts one process per selected file; gather paths that arrive close together.
        _argsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _argsTimer.Tick += (_, _) =>
        {
            _argsTimer.Stop();
            var paths = _pendingPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _pendingPaths.Clear();
            OpenFor(paths, ExplorerSelection.CursorOrCenter());
        };

        // While a drag is in progress, sample the Explorer folder the cursor crosses about every 150 ms.
        // The folder under the cursor when the wheel opens is often the source again, so the last folder
        // the user actually pointed at is the better target for the drop.
        _dragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _dragTimer.Tick += (_, _) => SampleDrag();

        input.IsWheelKey = key => !_settings.Paused && _settings.WheelFor(key) is not null;
        input.ModifierTapped += OnModifierTapped;
        input.LeftButtonUp += () => _wheel?.OnDragEnded();
        input.LeftButtonUp += () => _dragTimer.Stop();
        input.MouseDown += point =>
        {
            // Where the drag starts is the source folder, which the label needs to tell "here" from
            // "next to the file". The file itself is only known after it lands on the wheel.
            _dragOriginFolder = ShellFolders.FolderAt(point);
            _dragMemory.BeginDrag();
            _dragTimer.Start();
            if (_wheel is { IsFinished: false, Mode: WheelMode.Click } wheel && !wheel.Contains(point))
                wheel.Finish();
        };
        input.EscapePressed += () =>
        {
            if (_wheel is { IsFinished: false, Mode: WheelMode.Drag } wheel)
                wheel.Finish();
        };
    }

    /// <summary>
    /// Rebuilds the operations after the UI language changed (labels are fixed when an operation is
    /// created) and refreshes an open wheel in place; nothing has to close or reopen.
    /// </summary>
    public void Relocalize()
    {
        _coreOperations = BuildOperations();
        _actions = FileActions.Build(_settings, _settings.Save);
        _wheel?.Relocalize();
    }

    /// <summary>The cross-platform catalog plus tools that need Windows itself (OCR) or a window (Crop).</summary>
    public static IReadOnlyList<Operation> BuildOperations() =>
    [
        .. Catalog.Build(),
        new Ocr.OcrOperation(),
        new UI.Tools.Image.CropOperation(),
        new UI.Tools.Image.Frame.AddBackgroundOperation(),
        new UI.Tools.Image.EditPhotosOperation(),
        new UI.Tools.Collage.CollageOperation(),
    ];

    /// <summary>
    /// What fits these items. Conversions that need FFmpeg or Pandoc stay on the wheel even before those are
    /// downloaded; choosing one offers the download first.
    /// </summary>
    private IReadOnlyList<Operation> Operations(WheelKind wheel, IReadOnlyList<FileItem> items)
    {
        var offered = new List<Operation>();
        foreach (var op in wheel == WheelKind.Actions ? _actions : _coreOperations)
        {
            if (op.Wheel != wheel || !op.AppliesTo(items))
                continue;
            if (op.IsAvailable(_tools, items))
                offered.Add(op);
            else if (MissingDownloads(op, items) is { } missing)
                offered.Add(new NeedsToolOperation(op, missing));
        }
        return WheelLayout.Arrange(offered, _settings.HiddenOperations, _settings.FavoriteOperations);
    }

    /// <summary>The smallest set of downloadable tools that would make <paramref name="op"/> work, or null.</summary>
    private IReadOnlyList<ExternalTool>? MissingDownloads(Operation op, IReadOnlyList<FileItem> items)
    {
        var missing = ToolInstaller.Installable.Where(t => !_tools.Has(t)).ToList();
        if (missing.Count == 0)
            return null;
        foreach (var candidate in missing.Select(t => new[] { t }).Append(missing.ToArray()))
        {
            if (op.IsAvailable(_tools.Assuming(candidate), items))
                return candidate;
        }
        return null;
    }

    /// <summary>Every operation the wheels can show (for the wheel editor).</summary>
    public IReadOnlyList<Operation> AllOperations => [.. _coreOperations, .. _actions];

    private Operation? LastFor(IReadOnlyList<FileItem> items)
    {
        var id = _settings.LastOperationId;
        var op = id is null ? null : AllOperations.FirstOrDefault(o => o.Id == id);
        return op is not null && op.AppliesTo(items) && op.IsAvailable(_tools, items) ? op : null;
    }

    /// <summary>Samples the folder under the cursor while the user is dragging a file.</summary>
    private void SampleDrag()
    {
        if (!_input.IsDragging)
            return;
        var folder = ShellFolders.FolderAt(_input.Cursor);
        _dragMemory.Observe(folder, Directory.Exists, OutputTarget.CanWrite);
        UpdateDestination(folder);
    }

    /// <summary>Keeps the wheel's "save to" label in step with the folder the cursor is over.</summary>
    private void UpdateDestination(string? cursorFolder)
    {
        if (_wheel is not { IsFinished: false } wheel)
            return;

        var label = DropTargetLabels.Choose(
            _wheelSourceFolder, cursorFolder, _dragMemory.Remembered, Directory.Exists, OutputTarget.CanWrite);
        var text = label.Kind switch
        {
            DropTargetLabelKind.Folder => T("wheel.saveTo", UI.Bidi.Value(label.FolderName ?? string.Empty)),
            DropTargetLabelKind.Blocked => T("wheel.saveBlocked"),
            _ => T("wheel.saveNextTo"),
        };
        wheel.SetDestination(text, label.Folder, label.Kind == DropTargetLabelKind.Blocked);
    }

    private void OnModifierTapped(ModifierKey key)
    {
        if (_settings.Paused || _settings.WheelFor(key) is not { } wheel)
            return;
        if (_wheel is { IsFinished: false } open)
        {
            // Same key again: toggle "repeat last"; another key: switch wheel.
            if (wheel == open.CurrentWheel)
                open.ToggleRepeat();
            else
                open.SwitchTo(wheel);
            return;
        }
        if (_input.IsDragging)
            Open(WheelMode.Drag, wheel, null, _input.Cursor);
    }

    public void OnHotkey()
    {
        var paths = ExplorerSelection.Get(Win32.GetForegroundWindow());
        if (paths.Count == 0)
        {
            Notifications.Show(T("error.selection.none.title"),
                T("error.selection.none.detail"));
            return;
        }
        OpenFor(paths, ExplorerSelection.CursorOrCenter());
    }

    /// <summary>Opens the wheel for whatever is on the clipboard (tray menu).</summary>
    public void OpenForClipboard()
    {
        var paths = ExplorerSelection.FromClipboard();
        if (paths.Count == 0)
        {
            Notifications.Show(T("error.clipboard.empty.title"),
                T("error.clipboard.empty.detail"));
            return;
        }
        OpenFor(paths, ExplorerSelection.CursorOrCenter());
    }

    /// <summary>Paths from the context menu (possibly one call per file).</summary>
    public void QueuePaths(IEnumerable<string> paths)
    {
        _pendingPaths.AddRange(paths);
        _argsTimer.Stop();
        _argsTimer.Start();
    }

    public void OpenFor(IReadOnlyList<string> paths, Win32.POINT at)
    {
        var items = paths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p => new FileItem(p))
            .ToList();
        if (items.Count == 0)
            return;
        Open(WheelMode.Click, WheelKind.Convert, items, at);
    }

    /// <summary>Runs a wheel choice: a floating tool opens its window, everything else becomes a job.</summary>
    private void RunOperation(Operation op, IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        if (op is UI.Tools.IInteractiveTool tool)
            tool.Open(items, targets);
        else
            _jobs.StartInto(op, items, null, targets);
    }

    private void Open(WheelMode mode, WheelKind wheel, IReadOnlyList<FileItem>? items, Win32.POINT at)
    {
        _wheel?.Finish();
        UI.Theme.Refresh();
        // The folder under the cursor when the wheel opens, plus the last usable folder seen while the
        // drag was running. In drag mode the file itself is only known after it lands on the wheel, so
        // both candidates are validated against the real item when the operation starts (JobRunner).
        var targets = new DropTargets(ShellFolders.FolderAt(at), _dragMemory.Remembered);
        // "Next to the file" is the folder that owns the item; in drag mode the source folder is the one
        // the drag started from (the item itself only arrives with the drop).
        _wheelSourceFolder = items is { Count: > 0 }
            ? OutputTarget.SourceFolder(items[0].Path)
            : _dragOriginFolder;
        var window = new WheelWindow(mode, wheel, items, _settings, Operations,
            (op, list, dropTargets) => RunOperation(op, list, dropTargets), targets)
        {
            LastOperation = LastFor,
            RunRepeat = repeatItems =>
            {
                if (LastFor(repeatItems) is { } last)
                    RunOperation(last, repeatItems, targets);
            },
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_wheel, window))
                _wheel = null;
        };
        _wheel = window;
        UpdateDestination(targets.UnderCursor);
        window.ShowAt(at);
    }
}
