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
            // In drag mode the wheel never takes focus, so this global hook is its only Escape path.
            // The window applies the same two-step rule: clear the inner ring first, then close.
            if (_wheel is { IsFinished: false, Mode: WheelMode.Drag } wheel)
                wheel.Escape();
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
        new UI.Tools.Image.AnnotateOperation(),
        new UI.Tools.Image.RedactOperation(),
        new UI.Tools.Image.Frame.AddBackgroundOperation(),
        new UI.Tools.Image.RemoveBackgroundOperation(),
        new UI.Tools.Image.EditPhotosOperation(),
        new UI.Tools.Collage.CollageOperation(),
        new UI.Tools.Video.VideoFramesOperation(),
        new UI.Tools.Video.VideoRedactOperation(),
        new UI.Tools.Video.VideoSplitOperation(),
        new UI.Tools.Video.VideoJoinOperation(),
        new UI.Tools.Video.VideoTrimOperation(),
        new UI.Tools.Video.VideoCropOperation(),
        new UI.Tools.Video.VideoSpeedOperation(),
        new UI.Tools.Lock.LockOperation(),
        new UI.Tools.Password.ProtectOperation(),
        new UI.Tools.Compress.CompressOperation(),
        new UI.Tools.Meta.MetadataOperation(),
        new UI.Tools.Qr.QrOperation(),
        new UI.Tools.Pdf.PdfMergeOperation(),
        new UI.Tools.Pdf.PdfOrganizeOperation(),
        new UI.Tools.Audio.AudioNormalizeOperation(),
        new UI.Tools.Audio.AudioTrimOperation(),
        new UI.Tools.Audio.AudioChannelsOperation(),
        new UI.Tools.Audio.AudioBleepOperation(),
        new UI.Tools.Audio.AudioVisualizerOperation(),
    ];

    /// <summary>
    /// What fits these items. A conversion whose backend is missing stays visible as a dimmed "get …"
    /// slice, including backends installed outside the app (LibreOffice), so it never silently disappears;
    /// choosing it explains what to install.
    /// </summary>
    private IReadOnlyList<Operation> Operations(WheelKind wheel, IReadOnlyList<FileItem> items) =>
        Offered(wheel, items, _tools, _settings, wheel == WheelKind.Actions ? _actions : _coreOperations);

    /// <summary>The visible slices for a wheel, including the dimmed install/download variants.</summary>
    internal static IReadOnlyList<Operation> Offered(
        WheelKind wheel,
        IReadOnlyList<FileItem> items,
        ToolLocator tools,
        AppSettings settings,
        IReadOnlyList<Operation> operations)
    {
        var offered = new List<Operation>();
        foreach (var op in operations)
        {
            if (op.Wheel != wheel || !op.AppliesTo(items))
                continue;
            if (op.IsAvailable(tools, items))
                offered.Add(op);
            else if (MissingDownloads(op, items, tools) is { } missing)
                offered.Add(new NeedsToolOperation(op, missing));
        }
        return WheelCategories.Arrange(WheelLayout.Arrange(offered, settings.HiddenOperations, settings.FavoriteOperations));
    }

    /// <summary>The smallest set of tools that would make <paramref name="op"/> work, or null.</summary>
    internal static IReadOnlyList<ExternalTool>? MissingDownloads(Operation op, IReadOnlyList<FileItem> items, ToolLocator tools)
    {
        // LibreOffice is not downloaded by DropWheel, but it is still a backend an operation can need
        // (documents to PDF). Including it keeps that slice visible and says what to install instead of
        // silently dropping the conversion.
        var missing = ToolInstaller.Installable
            .Append(ExternalTool.LibreOffice)
            .Distinct()
            .Where(t => !tools.Has(t))
            .ToList();
        if (missing.Count == 0)
            return null;
        foreach (var candidate in missing.Select(t => new[] { t }).Append(missing.ToArray()))
        {
            if (op.IsAvailable(tools.Assuming(candidate), items))
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
        Open(WheelMode.Click, InitialWheel(items, AllOperations, _tools, _settings), items, at);
    }

    /// <summary>
    /// The wheel to show for a selection: Convert when it has anything, otherwise Tools (a folder only has
    /// pack there, grouped under Archive) and then Actions. The Convert ring used to open empty for folders
    /// and unknown extensions, which looked like a broken wheel rather than a deliberate "nothing to convert".
    /// </summary>
    internal static WheelKind InitialWheel(
        IReadOnlyList<FileItem> items,
        IReadOnlyList<Operation> operations,
        ToolLocator tools,
        AppSettings settings)
    {
        foreach (var wheel in new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions })
        {
            if (Offered(wheel, items, tools, settings, operations).Count > 0)
                return wheel;
        }
        // No wheel has a slice: the window's own "Nothing for this selection" state is the clearest answer.
        return WheelKind.Convert;
    }

    /// <summary>Runs a wheel choice: a floating tool opens its window, everything else becomes a job.</summary>
    private void RunOperation(Operation op, IReadOnlyList<FileItem> items, DropTargets? targets) =>
        Dispatch(op, items, targets, _jobs);

    /// <summary>
    /// The one place a wheel choice becomes work. Interactive tools are still remembered for "repeat last"
    /// even though they do not go through <see cref="JobRunner.Start"/>; jobs remember themselves there.
    /// </summary>
    internal static void Dispatch(
        Operation op,
        IReadOnlyList<FileItem> items,
        DropTargets? targets,
        JobRunner jobs,
        bool saveSettings = true)
    {
        if (op is WheelGroupOperation)
            throw new InvalidOperationException("A wheel group opens its chooser; it never runs as a job.");
        if (op is UI.Tools.IInteractiveTool tool)
        {
            jobs.Remember(op, null, saveSettings);
            tool.Open(items, targets);
        }
        else
        {
            jobs.StartInto(op, items, null, targets);
        }
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
            OpenGroup = (group, groupItems, parentIndex, parentAngle) =>
                ShowGroupChooser(group, groupItems, at, targets, parentIndex, parentAngle),
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

    /// <summary>
    /// Opens the two-ring wheel again after a drop closed it, with this category's inner ring seeded. It
    /// is the same window type at the same anchor with the same items; the outer ring keeps every
    /// category, so this is not a view swap. Escape still clears the inner ring before closing.
    /// </summary>
    private void ShowGroupChooser(
        WheelGroupOperation group,
        IReadOnlyList<FileItem> items,
        Win32.POINT at,
        DropTargets targets,
        int parentIndex,
        double parentAngle)
    {
        if (items.Count == 0)
            return;
        var window = new WheelWindow(WheelMode.Click, group.Wheel, items, _settings, Operations,
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
        window.ShowAt(at, group.GroupId, parentIndex, parentAngle);
    }
}
