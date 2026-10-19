using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using PdfDocument = PdfSharp.Pdf.PdfDocument;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;
using PdfReaderException = PdfSharp.Pdf.IO.PdfReaderException;
using WpfImage = System.Windows.Controls.Image;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Pdf;

/// <summary>
/// "Organize Pages" on the Tools wheel: one PDF becomes a grid of page tiles that can be rotated,
/// duplicated, deleted and dragged into a new order before the result is written as a new file.
/// </summary>
internal sealed class PdfOrganizeTool : IToolWindowSpec
{
    public static readonly PdfOrganizeTool Instance = new();

    /// <summary>About 620 x 700 DIP at the default preview box; the page grid scrolls inside it.</summary>
    public static readonly ToolSpecLayout Size = new(
        ChromeWidth: 40,
        ChromeHeight: 300,
        PreviewMinWidth: 360,
        PreviewMinHeight: 260,
        PreviewMaxWidth: 900,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 580,
        DefaultPreviewHeight: 400);

    public string Id => "tools.pdforganize";

    public string TitleKey => "tool.pdforganize.title";

    public ToolSpecLayout Layout => Size;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && PdfOrganize.CanOrganize(items[0]);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new PdfOrganizeView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class PdfOrganizeToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(PdfOrganizeTool.Instance);
}

/// <summary>The window content: a wrapping page grid, drag-reorder and the per-page rotate/copy/delete actions.</summary>
internal sealed class PdfOrganizeView
{
    private const string DragFormat = "DropWheel.PdfOrganizePage";
    private const double DragThreshold = 8;
    private const double CellWidth = 128;
    private const int ThumbWidth = 140;

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly Task<int> _pageCount;
    private readonly Dictionary<int, BitmapSource?> _thumbnails = [];
    private readonly List<(Border Cell, int Position, int SourceIndex, WpfImage Image, TextBlock Fallback)> _cells = [];
    private readonly WrapPanel _grid = new();
    private readonly TextBlock _status = new()
    {
        FontSize = 13,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _summary = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private PdfPagePlan? _plan;
    private Point _dragStart;
    private int _dragFrom = -1;

    public PdfOrganizeView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        // The page count is a cheap probe but still belongs off the UI thread.
        _pageCount = Task.Run(() => PdfBackend.PageCount(item.Path));
    }

    public ToolContent Build()
    {
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        var hint = Controls.Text(T("tool.pdforganize.hint"), 12, brush: Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hint, Dock.Left);
        DockPanel.SetDock(_summary, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(_summary);

        _grid.AllowDrop = true;
        _grid.Background = Brushes.Transparent;
        _grid.Margin = new Thickness(0, 0, 0, 4);
        _grid.DragOver += (_, e) =>
        {
            if (DragIndex(e.Data) is not null)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };
        _grid.Drop += (_, e) =>
        {
            if (DragIndex(e.Data) is not int from || _plan is null)
                return;
            MovePage(from, _plan.Count - 1);
            e.Handled = true;
        };

        var content = new Grid();
        content.Children.Add(_grid);
        content.Children.Add(_status);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(head, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(head);
        root.Children.Add(scroll);

        ShowStatus(T("tool.pdforganize.hint"));
        return new ToolContent(root, Reset, ApplyAsync, Task.FromResult<MediaSize?>(null), ActivateAsync);
    }

    /// <summary>Reads the page count, then renders the page thumbnails one at a time.</summary>
    public async Task ActivateAsync()
    {
        if (_plan is null)
        {
            int pageCount;
            try
            {
                pageCount = await _pageCount;
            }
            catch
            {
                ShowStatus(T("tool.pdforganize.error.open", _item.Name));
                return;
            }

            if (pageCount < 1)
            {
                ShowStatus(T("tool.pdforganize.error.plan"));
                return;
            }

            _plan = new PdfPagePlan(_item.Path, pageCount);
            Rebuild();
        }

        // One read for every page thumbnail; PDFium then only renders.
        byte[] bytes;
        try
        {
            bytes = await Task.Run(() => File.ReadAllBytes(_item.Path));
        }
        catch
        {
            ShowStatus(T("tool.pdforganize.error.open", _item.Name));
            return;
        }

        for (var index = 0; index < _plan.SourcePageCount; index++)
        {
            if (_thumbnails.ContainsKey(index))
                continue;
            BitmapSource? bitmap = null;
            try
            {
                bitmap = await Task.Run(() => PdfThumbnails.FromPdf(bytes, index, ThumbWidth));
            }
            catch
            {
                // Keep the tile; it shows that its preview is unavailable.
                bitmap = null;
            }
            _thumbnails[index] = bitmap;
            PaintThumbnail(index);
        }
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _cells.Clear();
        if (_plan is null)
            return;

        for (var position = 0; position < _plan.Count; position++)
            _grid.Children.Add(BuildCell(_plan.Pages[position], position));

        _summary.Text = Loc.Items(_plan.Count);
        _summary.Visibility = Visibility.Visible;
        HideStatus();
        foreach (var index in _thumbnails.Keys)
            PaintThumbnail(index);
    }

    private Border BuildCell(PdfPageRef page, int position)
    {
        var (thumbnail, image, fallback) = PdfToolChrome.Thumbnail(108, 132);
        image.RenderTransform = new RotateTransform(page.Rotation);

        var number = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Theme.Accent,
            Child = new TextBlock
            {
                Text = (position + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                Foreground = Theme.TextOnAccent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var overlay = new DockPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4) };
        DockPanel.SetDock(number, Dock.Left);
        overlay.Children.Add(number);
        if (page.Rotation != 0)
        {
            var rotation = new TextBlock
            {
                Text = $"{page.Rotation}\u00B0",
                FontSize = 10,
                Foreground = Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(rotation, Dock.Right);
            overlay.Children.Add(rotation);
        }

        var tile = new Grid();
        tile.Children.Add(thumbnail);
        tile.Children.Add(overlay);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        actions.Children.Add(PdfToolChrome.IconButton(Glyphs.Rotate, T("tool.pdforganize.rotate"), () =>
        {
            _plan?.Rotate(position, 90);
            Rebuild();
        }));
        actions.Children.Add(PdfToolChrome.IconButton(Glyphs.Copy, T("tool.pdforganize.duplicate"), () =>
        {
            _plan?.Duplicate(position);
            Rebuild();
        }));
        actions.Children.Add(PdfToolChrome.IconButton(Glyphs.Clear, T("tool.pdforganize.delete"), () =>
        {
            _plan?.Delete(position);
            Rebuild();
        }, enabled: _plan?.CanDelete ?? false));

        var content = new StackPanel();
        content.Children.Add(tile);
        content.Children.Add(actions);

        var cell = new Border
        {
            Width = CellWidth,
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 6, 6),
            Child = content,
            Tag = page.SourceIndex,
            AllowDrop = true,
        };

        // Drag the tile onto another one to move it there; dropping on empty grid space appends it.
        cell.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(cell);
            _dragFrom = position;
        };
        cell.MouseLeftButtonUp += (_, _) => _dragFrom = -1;
        cell.MouseMove += (_, e) =>
        {
            if (_dragFrom != position || e.LeftButton != MouseButtonState.Pressed)
                return;
            var moved = e.GetPosition(cell) - _dragStart;
            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
                return;
            var data = new DataObject(DragFormat, position);
            _dragFrom = -1;
            DragDrop.DoDragDrop(cell, data, DragDropEffects.Move);
        };
        cell.DragOver += (_, e) =>
        {
            if (DragIndex(e.Data) is null)
                return;
            cell.BorderBrush = Theme.Accent;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        cell.DragLeave += (_, _) => cell.BorderBrush = Theme.PanelBorder;
        cell.Drop += (_, e) =>
        {
            if (DragIndex(e.Data) is not int from)
                return;
            MovePage(from, position);
            e.Handled = true;
        };

        _cells.Add((cell, position, page.SourceIndex, image, fallback));
        return cell;
    }

    private void MovePage(int from, int to)
    {
        if (_plan is not null && _plan.Move(from, to))
            Rebuild();
    }

    private static int? DragIndex(IDataObject data) =>
        data.GetDataPresent(DragFormat) && data.GetData(DragFormat) is int value ? value : null;

    private void PaintThumbnail(int sourceIndex)
    {
        foreach (var (_, _, cellSourceIndex, image, fallback) in _cells)
        {
            if (cellSourceIndex != sourceIndex)
                continue;
            if (_thumbnails.TryGetValue(sourceIndex, out var bitmap) && bitmap is not null)
            {
                image.Source = bitmap;
                image.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            else if (_thumbnails.ContainsKey(sourceIndex))
            {
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                fallback.Text = T("tool.pdforganize.thumbFailed");
            }
        }
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
        _grid.Visibility = Visibility.Collapsed;
        _summary.Visibility = Visibility.Collapsed;
    }

    private void HideStatus()
    {
        _status.Visibility = Visibility.Collapsed;
        _grid.Visibility = Visibility.Visible;
    }

    private void Reset()
    {
        _plan?.Reset();
        Rebuild();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_plan is null || _plan.Count == 0)
            return null;
        if (!_plan.Validate().IsValid)
            throw new ConversionException(T("tool.pdforganize.error.plan"));

        var pages = _plan.Pages.ToList();
        var directory = PdfToolOutput.DirectoryFor(_item, _targets);
        return await OutputPaths.ProduceAsync(directory, $"{_item.BaseName} {T("tool.pdforganize.suffix")}", "pdf",
            temporary => Task.Run(() => PdfPageWriter.Write(_item.Path, pages, temporary)));
    }
}

/// <summary>
/// Writes an ordered page list from one PDF: the source is opened read-only once and every entry imports
/// its page again, so a duplicated page becomes an independent page in the result. The source file is
/// never modified.
/// </summary>
internal static class PdfPageWriter
{
    public static void Write(string sourcePath, IReadOnlyList<PdfPageRef> pages, string output)
    {
        PdfDocument source;
        try
        {
            source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is PdfReaderException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new ConversionException(T("tool.pdforganize.error.open", Path.GetFileName(sourcePath)), ex);
        }

        using (source)
        using (var result = new PdfDocument())
        {
            foreach (var reference in pages)
            {
                var added = result.AddPage(source.Pages[reference.SourceIndex]);
                var rotation = PdfOrganize.NormalizeRotation(added.Rotate + reference.Rotation);
                if (rotation != 0)
                    added.Rotate = rotation;
            }
            result.Save(output);
        }
    }
}
