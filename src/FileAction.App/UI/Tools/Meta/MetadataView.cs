using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Meta;

/// <summary>
/// The metadata window content: a "Search metadata" box over the field list, grouped by section
/// (file, tracks, EXIF/IPTC/XMP, PDF info, chapters). Every tag the tool can write is rendered as an
/// editable value with a per-field remove button; the rest reads as label: value. The footer carries the
/// fixed note and "Remove All", the window's primary button is "Save", and the work runs asynchronously
/// so it can be cancelled from the window. Several files of the same kind share one tag form: the common
/// value is shown, only the rows the user changes are written, and chapters stay per file.
/// </summary>
internal sealed class MetadataView
{
    private readonly IReadOnlyList<FileItem> _items;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly MetadataKind _kind;
    private readonly bool _batch;
    private readonly Task<MetadataBatchRead> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MetadataEdit _edit;

    private readonly TextBlock _title = new();
    private readonly TextBlock _meta = ToolControls.Label(string.Empty, 11.5);
    private readonly TextBox _search = ToolControls.Box(string.Empty, double.NaN);
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = ToolControls.Label(string.Empty, 11.5);
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Border _cancel;

    private MetadataDocument _document = MetadataDocument.Empty(MetadataKind.None);
    private IReadOnlyList<double> _durations = [];
    private double _duration;
    private CancellationTokenSource? _cts;
    private bool _running;

    public MetadataView(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _items = items;
        _targets = targets;
        _batch = items.Count > 1;
        _kind = MetadataBatch.KindOf(items);
        _edit = new MetadataEdit { ApplyOnlyChanged = _batch };
        _cancel = Controls.Button(T("tool.metadata.cancel"), () => _cts?.Cancel());
        // Read off the UI thread: the window opens immediately and fills in when the files have been read.
        _load = MetadataRunner.ReadManyAsync(items, _kind, _tools, CancellationToken.None);
        _search.TextChanged += (_, _) => RenderList();
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        for (var i = 0; i < 5; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var header = BuildHeader();
        var search = BuildSearch();
        var listHost = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 6, 0, 0),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list,
            },
        };
        var footer = BuildFooter();
        var status = BuildStatus();

        Grid.SetRow(header, 0);
        Grid.SetRow(search, 1);
        Grid.SetRow(listHost, 2);
        Grid.SetRow(footer, 3);
        Grid.SetRow(status, 4);
        root.Children.Add(header);
        root.Children.Add(search);
        root.Children.Add(listHost);
        root.Children.Add(footer);
        root.Children.Add(status);

        RenderList();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Fills the field list and seeds the editable values from what the files already have.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var read = await _load;
            var documents = read.Documents;
            _durations = read.Durations;

            if (_batch)
            {
                // One shared tag form: values every file agrees on, blank where they differ.
                _document = new MetadataDocument(_kind, MetadataBatch.SharedFields(documents));
                _meta.Text = T("tool.metadata.count", _document.Count);
            }
            else
            {
                _document = documents.Count > 0 ? documents[0] : MetadataDocument.Empty(_kind);
                _duration = _durations.Count > 0 ? _durations[0] : 0;
                var format = _items.Count > 0 ? _items[0].Format : string.Empty;
                _meta.Text = $"{Bidi.Value(_kind == MetadataKind.Pdf ? "PDF" : Formats.Display(Formats.Normalize(format)))} · {T("tool.metadata.count", _document.Count)}";
            }

            SeedEdit();
            RenderList();
        }
        catch (Exception ex)
        {
            _meta.Text = ex.Message;
        }
        finally
        {
            // This tool has no media aspect to adapt to: keep the declared panel size.
            _ready.TrySetResult(null);
        }
    }

    // ------------------------------------------------------------------ layout

    private FrameworkElement BuildHeader()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
        _title.Text = _batch || _items.Count == 0 ? Items(_items.Count) : Bidi.Value(_items[0].Name);
        _title.FontSize = 15;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Foreground = Theme.Text;
        _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _title.TextWrapping = TextWrapping.NoWrap;
        _meta.Text = T("tool.metadata.probe");
        _meta.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(_title);
        stack.Children.Add(_meta);
        return stack;
    }

    private FrameworkElement BuildSearch()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        stack.Children.Add(ToolControls.Label(T(MetadataDefaults.SearchKey), 11.5));
        _search.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_search);
        return stack;
    }

    private FrameworkElement BuildFooter()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var note = ToolControls.Label(T(MetadataDefaults.NoteKey), 10.5);
        note.Opacity = 0.85;
        stack.Children.Add(note);

        var removeAll = Controls.Button(T(MetadataDefaults.RemoveAllKey), ToggleRemoveAll);
        removeAll.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(removeAll);
        return stack;
    }

    private FrameworkElement BuildStatus()
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 2), LastChildFill = true };

        _cancel.Visibility = Visibility.Collapsed;
        _cancel.Margin = new Thickness(12, 0, 0, 0);
        _cancel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_cancel, Dock.Right);
        row.Children.Add(_cancel);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _status.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_progress);
        stack.Children.Add(_status);
        row.Children.Add(stack);
        return row;
    }

    /// <summary>Renders the grouped rows, filtered by the search box.</summary>
    private void RenderList()
    {
        _list.Children.Clear();

        if (_document.Count == 0)
        {
            _list.Children.Add(Muted(T("tool.metadata.hint.empty")));
            return;
        }

        var groups = _document.Grouped(_search.Text);
        if (groups.Count == 0)
        {
            _list.Children.Add(Muted(T("tool.metadata.search.empty", Bidi.Value(_search.Text.Trim()))));
            return;
        }

        foreach (var group in groups)
        {
            _list.Children.Add(new TextBlock
            {
                Text = T(group.GroupKey),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Accent,
                Margin = new Thickness(0, 10, 0, 4),
            });

            foreach (var field in group.Fields)
                _list.Children.Add(field.Editable ? EditableRow(field) : ReadOnlyRow(field));
        }
    }

    private FrameworkElement ReadOnlyRow(MetadataField field)
    {
        var row = NewRow();
        var label = LabelCell(field.Label);
        var value = new TextBlock
        {
            Text = Bidi.Value(field.Value),
            FontSize = 12,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(label);
        row.Children.Add(value);
        return row;
    }

    private FrameworkElement EditableRow(MetadataField field)
    {
        var key = field.EditKey!;
        var row = NewRow();
        var label = LabelCell(field.Label);

        var multiLine = key == MetadataEditKeys.Chapters;
        var box = ToolControls.Box(_edit.Get(key), double.NaN, multiline: multiLine);
        box.IsEnabled = !_edit.RemoveAll;
        box.TextChanged += (_, _) => _edit.Set(key, box.Text);

        var remove = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Theme.Track,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = multiLine ? VerticalAlignment.Top : VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = UiSetup.Tip(T(MetadataDefaults.RemoveFieldKey)),
            Child = new TextBlock
            {
                Text = "\uE738",
                FontFamily = Theme.Icons,
                FontSize = 10,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        remove.MouseLeftButtonUp += (_, _) =>
        {
            // Clear the box first (its TextChanged clears the "removed" mark), then mark the removal.
            box.Text = string.Empty;
            _edit.Remove(key);
        };

        Grid.SetColumn(label, 0);
        Grid.SetColumn(box, 1);
        Grid.SetColumn(remove, 2);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.Children.Add(label);
        row.Children.Add(box);
        row.Children.Add(remove);
        return row;
    }

    private static Grid NewRow()
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return row;
    }

    private static TextBlock LabelCell(string label) => new()
    {
        Text = Bidi.Value(label),
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static FrameworkElement Muted(string text)
    {
        var block = ToolControls.Label(text, 10.5);
        block.Margin = new Thickness(0, 4, 0, 0);
        block.Opacity = 0.85;
        return block;
    }

    // ------------------------------------------------------------------ state

    /// <summary>Seeds every editable row from the values the files already have.</summary>
    private void SeedEdit()
    {
        _edit.Reset();
        foreach (var field in _document.Fields)
        {
            if (field.EditKey is { } key)
                _edit.Seed(key, field.Value);
        }
    }

    private void ToggleRemoveAll()
    {
        _edit.RemoveAll = !_edit.RemoveAll;
        RenderList();
    }

    private void Reset()
    {
        _cts?.Cancel();
        _search.Text = string.Empty;
        SeedEdit();
        RenderList();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_running)
            return null;

        var issues = MetadataValidation.Validate(_kind, _edit);
        if (issues.Count > 0)
            throw new ConversionException(issues[0].Describe());
        if (!_edit.HasEdits)
            throw new ConversionException(T("tool.metadata.error.empty"));

        IReadOnlyList<MetadataChapter> chapters = Array.Empty<MetadataChapter>();
        if (!_batch && MetadataValidation.SupportsChapters(_kind) && !_edit.RemoveAll && !_edit.IsRemoved(MetadataEditKeys.Chapters))
        {
            chapters = MetadataValidation.ParseChapters(_edit.ChapterText, out var chapterIssues);
            if (chapterIssues.Count > 0)
                throw new ConversionException(chapterIssues[0].Describe());
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<double>(value =>
        {
            _progress.Value = Math.Clamp(value, 0, 1);
            _status.Text = T("tool.metadata.progress");
        });

        BeginRun();
        try
        {
            if (_batch)
            {
                var outputs = await MetadataRunner.WriteManyAsync(
                    _items, _kind, _edit, _durations, _targets, _tools, token, progress);
                // The shared window reports "what was created"; for a batch that is the number of copies.
                return outputs.Count == 0 ? null : Items(outputs.Count);
            }

            var directory = ToolOutputFolder.For(_items[0], _targets);
            return await MetadataRunner.WriteAsync(_items[0], _kind, _edit, chapters, _duration, directory, _tools, token, progress);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            EndRun();
        }
    }

    private void BeginRun()
    {
        _running = true;
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _status.Text = T("tool.metadata.progress");
        _cancel.Visibility = Visibility.Visible;
    }

    private void EndRun()
    {
        _running = false;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Value = 0;
        _status.Text = string.Empty;
        _cancel.Visibility = Visibility.Collapsed;
    }
}
