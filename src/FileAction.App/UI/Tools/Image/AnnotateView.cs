using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Shell;
using FileAction.Core;
using Microsoft.Win32;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The Annotate window body: the tool strip, the undo/z-order actions, the ink properties, the drawing
/// surface and the small text editor. It only arranges controls and forwards user intent; all object
/// geometry lives in <see cref="AnnotationSurface"/> and the pure Core model.
/// </summary>
internal sealed class AnnotateView
{
    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly Task<(BitmapSource Bitmap, int Width, int Height)> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AnnotationSurface _surface = new();
    private readonly List<(AnnotationTool Tool, Border Pill, TextBlock Label)> _toolButtons = [];
    private readonly List<(AnnotationColor Color, Border Swatch)> _swatches = [];
    private readonly List<(double Value, Border Pill, TextBlock Text)> _strokePills = [];
    private readonly List<(double Value, Border Pill, TextBlock Text)> _fontPills = [];
    private readonly List<(Border Button, Func<bool> Enabled)> _actions = [];

    private Grid? _previewHost;
    private Border? _placeholder;
    private Border? _editorShade;
    private Border? _textEditor;
    private TextBox? _textField;
    private Border? _fillPill;
    private TextBlock? _fillLabel;
    private AnnotationPoint _editingPoint;
    private int _editingIndex = -1;

    public AnnotateView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        // Decode off the UI thread: the window opens immediately with a placeholder.
        _load = Task.Run(() => AnnotateRunner.Load(item.Path, AnnotateRunner.PreviewMaxSide));
    }

    public ToolContent Build()
    {
        _surface.Changed += Refresh;
        _surface.SelectionChanged += Refresh;
        _surface.TextRequested += point => OpenEditor(point, -1, string.Empty, LanguageManager.IsRtl);
        _surface.TextEditRequested += EditExistingText;

        var root = new Grid { Margin = new Thickness(5, 0, 5, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tools = BuildTools();
        var actions = BuildActions();
        var colors = BuildColors();
        var sizes = BuildSizes();
        var preview = BuildPreview();

        Grid.SetRow(tools, 0);
        Grid.SetRow(actions, 1);
        Grid.SetRow(colors, 2);
        Grid.SetRow(sizes, 3);
        Grid.SetRow(preview, 4);
        root.Children.Add(tools);
        root.Children.Add(actions);
        root.Children.Add(colors);
        root.Children.Add(sizes);
        root.Children.Add(preview);

        // Keyboard shortcuts belong to the whole tool window, not to the drawing surface, so Delete and
        // Ctrl+Z work while any of the toolbar pills has the focus.
        root.Loaded += (_, _) =>
        {
            if (Window.GetWindow(root) is { } window)
                window.PreviewKeyDown += OnWindowKeyDown;
        };

        Refresh();
        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Replaces the placeholder with the decoded photo once the background decode finishes.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var (bitmap, width, height) = await _load;
            _surface.SetImage(bitmap, width, height);
            if (_placeholder is not null)
                _placeholder.Visibility = Visibility.Collapsed;
            _surface.Visibility = Visibility.Visible;
            Refresh();
            _ready.TrySetResult(new MediaSize(width, height));
        }
        catch (Exception ex)
        {
            if (_placeholder is not null)
            {
                _placeholder.Child = new TextBlock
                {
                    Text = ex.Message,
                    Foreground = Theme.Muted,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16),
                };
            }
            _ready.TrySetResult(null);
        }
    }

    // ------------------------------------------------------------------ toolbar

    private FrameworkElement BuildTools()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var tool in Enum.GetValues<AnnotationTool>())
        {
            var text = T(ToolKey(tool));
            var label = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 5, 9, 6),
                Margin = new Thickness(0, 0, 5, 5),
                Background = Theme.Track,
                Cursor = Cursors.Hand,
                Child = label,
                ToolTip = UiSetup.Tip(text),
            };
            var captured = tool;
            pill.MouseLeftButtonUp += (_, _) => OnToolClicked(captured);
            _toolButtons.Add((tool, pill, label));
            panel.Children.Add(pill);
        }
        return panel;
    }

    private void OnToolClicked(AnnotationTool tool)
    {
        if (tool == AnnotationTool.Stamp)
        {
            // First click loads an image; while the stamp tool is active the same button lets the user
            // pick a different image. Clicking it from another tool reuses the image already loaded.
            if (_surface.Tool == AnnotationTool.Stamp || !_surface.HasStamps)
            {
                _ = PickStampAsync();
                return;
            }
            _surface.SetTool(AnnotationTool.Stamp);
            Refresh();
            return;
        }

        _surface.SetTool(tool);
        Refresh();
    }

    private async Task PickStampAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = T("tool.annotate.stamp.pick"),
            Filter = T("tool.annotate.stamp.filter"),
            CheckFileExists = true,
            Multiselect = false,
        };
        var owner = Window.GetWindow(_surface);
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (accepted != true)
            return;

        try
        {
            var path = dialog.FileName;
            // 2048 px keeps the stamp sharp when it is placed large on a high-resolution photo.
            var (bitmap, _, _) = await Task.Run(() => AnnotateRunner.Load(path, 2048));
            _surface.AddStamp(bitmap);
            // The stamp pill remembers which image is loaded; the file name is isolated so a Latin name
            // in the Arabic UI keeps its left-to-right order.
            var stampPill = _toolButtons.FirstOrDefault(t => t.Tool == AnnotationTool.Stamp).Pill;
            if (stampPill is not null)
            {
                stampPill.ToolTip = UiSetup.Tip(
                    $"{T("tool.annotate.tool.stamp")} · {Bidi.Value(Path.GetFileName(path))}");
            }
            Refresh();
        }
        catch (Exception ex)
        {
            Notifications.Show(T("app.error.title"), T("error.image.process", ex.Message), error: true);
        }
    }

    private FrameworkElement BuildActions()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        AddAction(panel, T("tool.annotate.undo"), _surface.Undo, () => _surface.CanUndo);
        AddAction(panel, T("tool.annotate.redo"), _surface.Redo, () => _surface.CanRedo);
        AddAction(panel, T("tool.annotate.delete"), _surface.DeleteSelected, () => _surface.HasSelection);
        AddAction(panel, T("tool.annotate.front"), _surface.BringToFront, () =>
            _surface.HasSelection && _surface.SelectedIndex < (_surface.Document?.Count ?? 0) - 1);
        AddAction(panel, T("tool.annotate.back"), _surface.SendToBack, () =>
            _surface.HasSelection && _surface.SelectedIndex > 0);
        return panel;
    }

    private void AddAction(Panel panel, string label, Action click, Func<bool> enabled)
    {
        var button = Controls.Button(label, click);
        button.Margin = new Thickness(0, 0, 6, 4);
        panel.Children.Add(button);
        _actions.Add((button, enabled));
    }

    // ------------------------------------------------------------------ properties

    private FrameworkElement BuildColors()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(Label(T("tool.annotate.colors")));
        foreach (var color in AnnotationColor.Palette)
        {
            var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 0, 6, 4),
                Background = brush,
                BorderBrush = Theme.PanelBorder,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = UiSetup.Tip(color.ToHex()),
            };
            var captured = color;
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _surface.SetColor(captured);
                Refresh();
            };
            _swatches.Add((color, swatch));
            panel.Children.Add(swatch);
        }

        // Its own pill (instead of Controls.Toggle) so Reset can repaint it.
        _fillLabel = new TextBlock
        {
            Text = T("tool.annotate.fill"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fillPill = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 5, 9, 6),
            Margin = new Thickness(4, 0, 5, 4),
            Background = Theme.Track,
            Cursor = Cursors.Hand,
            Child = _fillLabel,
            ToolTip = UiSetup.Tip(T("tool.annotate.fill")),
        };
        _fillPill.MouseLeftButtonUp += (_, _) =>
        {
            _surface.SetFilled(!_surface.Filled);
            Refresh();
        };
        panel.Children.Add(_fillPill);
        return panel;
    }

    private FrameworkElement BuildSizes()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(Label(T("tool.annotate.stroke")));
        foreach (var width in new[] { 2.0, 4.0, 8.0 })
        {
            var value = width;
            AddNumberPill(panel, _strokePills, value, () =>
            {
                _surface.SetStrokeWidth(value);
                Refresh();
            });
        }

        panel.Children.Add(Label(T("tool.annotate.font")));
        foreach (var size in new[] { 14.0, 20.0, 30.0 })
        {
            var value = size;
            AddNumberPill(panel, _fontPills, value, () =>
            {
                _surface.SetFontSize(value);
                Refresh();
            });
        }
        return panel;
    }

    private static void AddNumberPill(
        Panel panel, List<(double Value, Border Pill, TextBlock Text)> pills, double value, Action click)
    {
        var label = new TextBlock
        {
            Text = value.ToString("0", CultureInfo.InvariantCulture),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 4, 9, 5),
            Margin = new Thickness(0, 0, 5, 4),
            Background = Theme.Track,
            Cursor = Cursors.Hand,
            Child = label,
        };
        pill.MouseLeftButtonUp += (_, _) => click();
        pills.Add((value, pill, label));
        panel.Children.Add(pill);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 4),
    };

    // ------------------------------------------------------------------ preview and text editor

    private FrameworkElement BuildPreview()
    {
        _surface.Visibility = Visibility.Collapsed;
        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
        };

        _textEditor = BuildEditor();
        // The shade blocks drawing while the text editor is open, so a stray click cannot add a second
        // object behind the dialog.
        var shadeBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
        shadeBrush.Freeze();
        _editorShade = new Border
        {
            Background = shadeBrush,
            Visibility = Visibility.Collapsed,
        };
        _previewHost = new Grid { Margin = new Thickness(0, 6, 0, 10), ClipToBounds = true };
        _previewHost.Children.Add(_surface);
        _previewHost.Children.Add(_placeholder);
        _previewHost.Children.Add(_editorShade);
        _previewHost.Children.Add(_textEditor);

        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Child = _previewHost,
        };
    }

    private Border BuildEditor()
    {
        _textField = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            MaxHeight = 150,
            Padding = new Thickness(8, 6, 8, 6),
            Background = Theme.Card,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _textField.PreviewKeyDown += OnTextKeyDown;
        _textField.TextChanged += (_, _) => UpdateTextDirection();

        var add = Controls.Button(T("tool.annotate.text.add"), CommitEditor, primary: true);
        var cancel = Controls.Button(T("action.cancel"), CloseEditor);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = LanguageManager.IsRtl ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);

        var title = new TextBlock
        {
            Text = T("tool.annotate.text.title"),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var hint = new TextBlock
        {
            Text = T("tool.annotate.text.hint"),
            FontSize = 11,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(_textField);
        panel.Children.Add(hint);
        panel.Children.Add(buttons);

        var editor = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(18),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Effect = Theme.Shadow(18),
            Child = panel,
        };
        return editor;
    }

    private void OpenEditor(AnnotationPoint point, int index, string text, bool rtl)
    {
        if (_textEditor is null || _textField is null)
            return;
        _editingPoint = point;
        _editingIndex = index;
        _textField.Text = text;
        _textField.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _textField.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
        _textEditor.Visibility = Visibility.Visible;
        if (_editorShade is not null)
            _editorShade.Visibility = Visibility.Visible;
        _textField.Focus();
        _textField.SelectAll();
    }

    private void EditExistingText(int index)
    {
        var document = _surface.Document;
        if (document is null || index < 0 || index >= document.Count)
            return;
        var obj = document.Objects[index];
        if (obj.Kind == AnnotationKind.Text)
            OpenEditor(default, index, obj.Text, obj.Rtl);
    }

    private void CommitEditor()
    {
        if (_textField is null)
            return;
        // Only line breaks and surrounding blanks are trimmed; inner spacing is the user's text.
        var text = _textField.Text.Trim();
        var rtl = ContainsArabic(text);
        if (text.Length > 0)
        {
            if (_editingIndex >= 0)
                _surface.ReplaceText(_editingIndex, text, rtl);
            else
                _surface.CreateText(_editingPoint, text, rtl);
        }
        CloseEditor();
        Refresh();
    }

    private void CloseEditor()
    {
        _editingIndex = -1;
        if (_textEditor is not null)
            _textEditor.Visibility = Visibility.Collapsed;
        if (_editorShade is not null)
            _editorShade.Visibility = Visibility.Collapsed;
        if (_textField is not null)
            _textField.Text = string.Empty;
    }

    private void OnTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Cancel keeps the tool window open; ToolWindow's Escape handler must not see this key.
            CloseEditor();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control)
        {
            CommitEditor();
            e.Handled = true;
        }
    }

    /// <summary>Arabic characters make the editor and the rendered object right-to-left.</summary>
    private void UpdateTextDirection()
    {
        if (_textField is null)
            return;
        var rtl = ContainsArabic(_textField.Text);
        _textField.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _textField.TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left;
    }

    /// <summary>True when the text contains Arabic letters, so it must be shaped right-to-left.</summary>
    internal static bool ContainsArabic(string? text) =>
        !string.IsNullOrEmpty(text) && text.Any(c => c is
            (>= '\u0600' and <= '\u06FF') or
            (>= '\u0750' and <= '\u077F') or
            (>= '\uFB50' and <= '\uFDFF') or
            (>= '\uFE70' and <= '\uFEFF'));

    // ------------------------------------------------------------------ actions

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // The text editor owns the keyboard while it is open (Delete must delete characters, not objects).
        if (_textEditor is { Visibility: Visibility.Visible })
            return;

        if (e.Key is Key.Delete or Key.Back)
        {
            _surface.DeleteSelected();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            _surface.Undo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            _surface.Redo();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            _surface.Redo();
            e.Handled = true;
        }
    }

    private void Reset()
    {
        CloseEditor();
        _surface.Reset();
        Refresh();
    }

    private async Task<string?> ApplyAsync()
    {
        // Text typed but not confirmed yet is part of what the user sees, so it is committed first.
        if (_textEditor is { Visibility: Visibility.Visible })
            CommitEditor();

        var document = _surface.Document;
        if (document is null)
            return null;
        if (document.Count == 0)
        {
            Notifications.Show(T("app.error.title"), T("tool.annotate.empty"), error: true);
            return null;
        }

        // The exporter gets an immutable snapshot, so a late edit cannot change what is being written.
        var snapshot = new AnnotationDocument(document.ImageWidth, document.ImageHeight);
        snapshot.Restore(document.Snapshot());
        return await AnnotateRunner.ApplyAsync(_item, snapshot, [.. _surface.Stamps], OutputDirectory());
    }

    /// <summary>Next to the source, or the folder this drop resolved (2.2.3 rules).</summary>
    private string OutputDirectory()
    {
        var detected = OutputTarget.Resolve(_item.Path, _targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && _targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(_item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(_item.Path)
            ?? Environment.CurrentDirectory;
    }

    // ------------------------------------------------------------------ state painting

    private void Refresh()
    {
        foreach (var (button, enabled) in _actions)
        {
            var on = enabled();
            button.IsEnabled = on;
            button.Opacity = on ? 1 : 0.45;
        }

        foreach (var (tool, pill, label) in _toolButtons)
        {
            var selected = tool == _surface.Tool;
            pill.Background = selected ? Theme.Accent : Theme.Track;
            label.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
        }

        foreach (var (color, swatch) in _swatches)
        {
            var selected = color == _surface.Color;
            swatch.BorderBrush = selected ? Theme.Text : Theme.PanelBorder;
            swatch.BorderThickness = new Thickness(selected ? 2 : 1);
        }

        PaintPills(_strokePills, _surface.StrokeWidth);
        PaintPills(_fontPills, _surface.FontSize);

        if (_fillPill is not null && _fillLabel is not null)
        {
            var on = _surface.Filled;
            _fillPill.Background = on ? Theme.Accent : Theme.Track;
            _fillLabel.Foreground = on ? Theme.TextOnAccent : Theme.Text;
        }
    }

    private static void PaintPills(List<(double Value, Border Pill, TextBlock Text)> pills, double current)
    {
        foreach (var (value, pill, text) in pills)
        {
            var selected = Math.Abs(value - current) < 0.001;
            pill.Background = selected ? Theme.Accent : Theme.Track;
            text.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
        }
    }

    private static string ToolKey(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Select => "tool.annotate.tool.select",
        AnnotationTool.Arrow => "tool.annotate.tool.arrow",
        AnnotationTool.Line => "tool.annotate.tool.line",
        AnnotationTool.Rectangle => "tool.annotate.tool.rectangle",
        AnnotationTool.Ellipse => "tool.annotate.tool.ellipse",
        AnnotationTool.Text => "tool.annotate.tool.text",
        AnnotationTool.Callout => "tool.annotate.tool.callout",
        AnnotationTool.Magnifier => "tool.annotate.tool.magnifier",
        AnnotationTool.Pen => "tool.annotate.tool.pen",
        AnnotationTool.Highlighter => "tool.annotate.tool.highlighter",
        AnnotationTool.Stamp => "tool.annotate.tool.stamp",
        _ => "tool.annotate.tool.select",
    };
}
