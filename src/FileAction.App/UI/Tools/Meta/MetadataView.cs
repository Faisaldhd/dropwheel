using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Meta;

/// <summary>
/// The metadata window content: a search box over the grouped field list (file / track / chapter), and an
/// editor for the tags the tool can write — camera and GPS for images, title/artist/album and chapters for
/// audio/video, the info dictionary for PDFs — plus "Remove all metadata". The work runs asynchronously
/// and can be cancelled from the window.
/// </summary>
internal sealed class MetadataView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly ToolLocator _tools = new();
    private readonly MetadataKind _kind;
    private readonly string _format;
    private readonly Task<MetadataRead> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MetadataEdit _edit = new();
    private readonly List<Control> _inputs = [];

    private readonly TextBlock _title = new();
    private readonly TextBlock _meta = ToolControls.Label(string.Empty, 11.5);
    private readonly TextBox _search = ToolControls.Box(string.Empty, double.NaN);
    private readonly StackPanel _list = new();
    private readonly StackPanel _editor = new();
    private readonly TextBlock _status = ToolControls.Label(string.Empty, 11.5);
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Border _cancel;

    private readonly TextBox _titleBox = ToolControls.Box();
    private readonly TextBox _artistBox = ToolControls.Box();
    private readonly TextBox _albumBox = ToolControls.Box();
    private readonly TextBox _authorBox = ToolControls.Box();
    private readonly TextBox _subjectBox = ToolControls.Box();
    private readonly TextBox _keywordsBox = ToolControls.Box();
    private readonly TextBox _makeBox = ToolControls.Box();
    private readonly TextBox _modelBox = ToolControls.Box();
    private readonly TextBox _latitudeBox = ToolControls.Box();
    private readonly TextBox _longitudeBox = ToolControls.Box();
    private readonly TextBox _chaptersBox = ToolControls.Box(string.Empty, double.NaN, multiline: true);

    private MetadataDocument _document = MetadataDocument.Empty(MetadataKind.None);
    private double _duration;
    private CancellationTokenSource? _cts;
    private bool _running;

    public MetadataView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        _format = Formats.Normalize(item.Format);
        _kind = MetadataModel.KindOf(_format);
        _cancel = Controls.Button(T("tool.metadata.cancel"), () => _cts?.Cancel());
        // Read off the UI thread: the window opens immediately and fills in when the file has been read.
        _load = MetadataRunner.ReadAsync(item, _kind, _tools, CancellationToken.None);
        _search.TextChanged += (_, _) => RenderList();
    }

    public ToolContent Build()
    {
        var root = new Grid { Margin = new Thickness(18, 6, 18, 6) };
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
        var editorHost = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new ScrollViewer
            {
                MaxHeight = 236,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _editor,
            },
        };
        var status = BuildStatus();

        Grid.SetRow(header, 0);
        Grid.SetRow(search, 1);
        Grid.SetRow(listHost, 2);
        Grid.SetRow(editorHost, 3);
        Grid.SetRow(status, 4);
        root.Children.Add(header);
        root.Children.Add(search);
        root.Children.Add(listHost);
        root.Children.Add(editorHost);
        root.Children.Add(status);

        RenderEditor();
        RenderList();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Fills the field list and seeds the editor from the values already in the file.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var read = await _load;
            _document = read.Document;
            _duration = read.DurationSeconds;
            _meta.Text = $"{Bidi.Value(_kind == MetadataKind.Pdf ? "PDF" : Formats.Display(_format))} · {T("tool.metadata.count", _document.Count)}";
            Prefill();
            SyncBoxes();
            RenderEditor();
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
        _title.Text = Bidi.Value(_item.Name);
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
        stack.Children.Add(ToolControls.Label(T("tool.metadata.search"), 11.5));
        _search.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(_search);
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

    /// <summary>Rebuilds the editor for the file kind; the same boxes are reused, never duplicated.</summary>
    private void RenderEditor()
    {
        _editor.Children.Clear();
        _inputs.Clear();

        switch (_kind)
        {
            case MetadataKind.Image:
                _editor.Children.Add(Fields(
                    (T("tool.metadata.field.cameraMake"), _makeBox),
                    (T("tool.metadata.field.cameraModel"), _modelBox)));
                _editor.Children.Add(Fields(
                    (T("tool.metadata.field.latitude"), _latitudeBox),
                    (T("tool.metadata.field.longitude"), _longitudeBox)));
                _editor.Children.Add(Muted(T("tool.metadata.hint.image")));
                break;

            case MetadataKind.Media:
                _editor.Children.Add(Fields(
                    (T("tool.metadata.field.title"), _titleBox),
                    (T("tool.metadata.field.artist"), _artistBox)));
                _editor.Children.Add(Fields((T("tool.metadata.field.album"), _albumBox)));
                _editor.Children.Add(Field(T("tool.metadata.field.chapters"), _chaptersBox, T("tool.metadata.chapters.hint")));
                _editor.Children.Add(Muted(T("tool.metadata.hint.media")));
                break;

            case MetadataKind.Pdf:
                _editor.Children.Add(Fields(
                    (T("tool.metadata.field.title"), _titleBox),
                    (T("tool.metadata.field.author"), _authorBox)));
                _editor.Children.Add(Fields(
                    (T("tool.metadata.field.subject"), _subjectBox),
                    (T("tool.metadata.field.keywords"), _keywordsBox)));
                _editor.Children.Add(Muted(T("tool.metadata.hint.pdf")));
                break;
        }

        _editor.Children.Add(BuildRemoveRow());
        TrackInputs();
        UpdateEditorState();
    }

    private FrameworkElement BuildRemoveRow()
    {
        var toggle = Controls.Toggle(_edit.RemoveAll, on =>
        {
            _edit.RemoveAll = on;
            UpdateEditorState();
        });

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Controls.Text(T("tool.metadata.removeAll"), 12.5));
        var hint = ToolControls.Label(T("tool.metadata.removeAll.hint"), 11);
        hint.Margin = new Thickness(0, 2, 0, 0);
        texts.Children.Add(hint);

        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = true };
        DockPanel.SetDock(toggle, Dock.Right);
        toggle.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(toggle);
        row.Children.Add(texts);
        return row;
    }

    /// <summary>A row of one or two labelled boxes.</summary>
    private static FrameworkElement Fields(params (string Label, TextBox Box)[] fields)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < fields.Length; i++)
        {
            var cell = Field(fields[i].Label, fields[i].Box, hint: null);
            cell.Margin = new Thickness(0, 0, 0, 6);
            Grid.SetColumn(cell, fields.Length == 1 ? 0 : i * 2);
            grid.Children.Add(cell);
        }

        if (fields.Length == 1)
            Grid.SetColumnSpan(grid.Children[0], 3);

        return grid;
    }

    private static FrameworkElement Field(string label, TextBox box, string? hint)
    {
        var stack = new StackPanel();
        var caption = ToolControls.Label(label, 11);
        caption.Margin = new Thickness(0, 0, 0, 3);
        stack.Children.Add(caption);
        box.Width = double.NaN;
        stack.Children.Add(box);
        if (!string.IsNullOrEmpty(hint))
        {
            var hintText = ToolControls.Label(hint, 10.5);
            hintText.Margin = new Thickness(0, 3, 0, 0);
            hintText.Opacity = 0.85;
            stack.Children.Add(hintText);
        }

        return stack;
    }

    private static FrameworkElement Muted(string text)
    {
        var block = ToolControls.Label(text, 10.5);
        block.Margin = new Thickness(0, 4, 0, 0);
        block.Opacity = 0.85;
        return block;
    }

    private void TrackInputs()
    {
        _inputs.AddRange([
            _titleBox, _artistBox, _albumBox, _authorBox, _subjectBox, _keywordsBox,
            _makeBox, _modelBox, _latitudeBox, _longitudeBox, _chaptersBox,
        ]);
    }

    private void UpdateEditorState()
    {
        foreach (var input in _inputs)
            input.IsEnabled = !_edit.RemoveAll;
    }

    /// <summary>Renders the grouped field list, filtered by the search box.</summary>
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
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = Bidi.Value(field.Label),
                    FontSize = 11.5,
                    Foreground = Theme.Muted,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                var value = new TextBlock
                {
                    Text = Bidi.Value(field.Value),
                    FontSize = 12,
                    Foreground = Theme.Text,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(label, 0);
                Grid.SetColumn(value, 1);
                row.Children.Add(label);
                row.Children.Add(value);
                _list.Children.Add(row);
            }
        }
    }

    // ------------------------------------------------------------------ state

    /// <summary>Seeds the editor from the values the file already has, so a small change keeps them.</summary>
    private void Prefill()
    {
        _edit.Title = Find(MetadataGroups.PdfKey, "title") ?? Find(MetadataGroups.FileKey, "title") ?? string.Empty;
        _edit.Artist = Find(MetadataGroups.FileKey, "artist") ?? string.Empty;
        _edit.Album = Find(MetadataGroups.FileKey, "album") ?? string.Empty;
        _edit.Author = Find(MetadataGroups.PdfKey, "author") ?? string.Empty;
        _edit.Subject = Find(MetadataGroups.PdfKey, "subject") ?? string.Empty;
        _edit.Keywords = Find(MetadataGroups.PdfKey, "keywords") ?? string.Empty;
        _edit.CameraMake = Find(MetadataGroups.ExifKey, "Make") ?? string.Empty;
        _edit.CameraModel = Find(MetadataGroups.ExifKey, "Model") ?? string.Empty;
        _edit.Latitude = string.Empty;
        _edit.Longitude = string.Empty;
        _edit.ChapterText = string.Empty;
    }

    private void SyncBoxes()
    {
        _titleBox.Text = _edit.Title;
        _artistBox.Text = _edit.Artist;
        _albumBox.Text = _edit.Album;
        _authorBox.Text = _edit.Author;
        _subjectBox.Text = _edit.Subject;
        _keywordsBox.Text = _edit.Keywords;
        _makeBox.Text = _edit.CameraMake;
        _modelBox.Text = _edit.CameraModel;
        _latitudeBox.Text = _edit.Latitude;
        _longitudeBox.Text = _edit.Longitude;
        _chaptersBox.Text = _edit.ChapterText;
    }

    private void CollectEdit()
    {
        _edit.Title = _titleBox.Text;
        _edit.Artist = _artistBox.Text;
        _edit.Album = _albumBox.Text;
        _edit.Author = _authorBox.Text;
        _edit.Subject = _subjectBox.Text;
        _edit.Keywords = _keywordsBox.Text;
        _edit.CameraMake = _makeBox.Text;
        _edit.CameraModel = _modelBox.Text;
        _edit.Latitude = _latitudeBox.Text;
        _edit.Longitude = _longitudeBox.Text;
        _edit.ChapterText = _chaptersBox.Text;
    }

    private string? Find(string groupKey, string label) => _document.Fields
        .FirstOrDefault(field => string.Equals(field.GroupKey, groupKey, StringComparison.Ordinal)
                                 && string.Equals(field.Label, label, StringComparison.OrdinalIgnoreCase))
        ?.Value;

    private void Reset()
    {
        _cts?.Cancel();
        _search.Text = string.Empty;
        _edit.RemoveAll = false;
        Prefill();
        SyncBoxes();
        RenderEditor();
        RenderList();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_running)
            return null;

        CollectEdit();
        var issues = MetadataValidation.Validate(_kind, _edit);
        if (issues.Count > 0)
            throw new ConversionException(issues[0].Describe());
        if (!_edit.HasEdits)
            throw new ConversionException(T("tool.metadata.error.empty"));

        IReadOnlyList<MetadataChapter> chapters = Array.Empty<MetadataChapter>();
        if (MetadataValidation.SupportsChapters(_kind) && !_edit.RemoveAll)
        {
            chapters = MetadataValidation.ParseChapters(_edit.ChapterText, out var chapterIssues);
            if (chapterIssues.Count > 0)
                throw new ConversionException(chapterIssues[0].Describe());
        }

        var directory = ToolOutputFolder.For(_item, _targets);
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
            return await MetadataRunner.WriteAsync(_item, _kind, _edit, chapters, _duration, directory, _tools, token, progress);
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
