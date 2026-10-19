using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using PdfDocument = PdfSharp.Pdf.PdfDocument;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfPage = PdfSharp.Pdf.PdfPage;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;
using PdfReaderException = PdfSharp.Pdf.IO.PdfReaderException;
using PdfRectangle = PdfSharp.Pdf.PdfRectangle;
using WpfImage = System.Windows.Controls.Image;
using XRect = PdfSharp.Drawing.XRect;
using XUnit = PdfSharp.Drawing.XUnit;
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

    public string PrimaryLabelKey => "tool.pdforganize.save";

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
    private readonly Grid _root = new();
    private WrapPanel _grid = new();
    private TextBlock _status = Status();
    private TextBlock _summary = Summary();

    private PdfPagePlan? _plan;
    private Point _dragStart;
    private int _dragFrom = -1;
    private bool _sameWidth;

    private static TextBlock Status() => new()
    {
        FontSize = 13,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    private static TextBlock Summary() => new()
    {
        FontSize = 13,
        Foreground = Theme.Text,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public PdfOrganizeView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        // The page count is a cheap probe but still belongs off the UI thread.
        _pageCount = Task.Run(() => PdfBackend.PageCount(item.Path));
    }

    public ToolContent Build()
    {
        BuildBody();
        ShowStatus(T("tool.pdforganize.hint"));
        // Windows can flip dark/light while the window is open; rebuild so every brush is the new palette.
        PdfThemeWatcher.Subscribe(RepaintForTheme);
        return new ToolContent(_root, Reset, ApplyAsync, Task.FromResult<MediaSize?>(null), ActivateAsync);
    }

    private void RepaintForTheme()
    {
        if (!_root.IsVisible)
            return;
        BuildBody();
        if (_plan is null)
            ShowStatus(T("tool.pdforganize.hint"));
        else
            Rebuild();
    }

    private void BuildBody()
    {
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _summary = Summary();
        _status = Status();
        _grid = new WrapPanel();

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        _summary.ToolTip = T("tool.pdforganize.hint");
        var resetOrder = Controls.Button(T("tool.pdforganize.resetOrder"), Reset);
        resetOrder.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(resetOrder, Dock.Right);
        head.Children.Add(resetOrder);
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

        var sameWidth = new CheckBox
        {
            Content = T("tool.pdforganize.sameWidth"),
            Foreground = Theme.Text,
            FontSize = 12,
            IsChecked = _sameWidth,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        sameWidth.Checked += (_, _) => _sameWidth = true;
        sameWidth.Unchecked += (_, _) => _sameWidth = false;
        var note = Controls.Text(T("tool.pdforganize.sameWidthNote"), 11, brush: Theme.Muted);
        note.Margin = new Thickness(0, 2, 0, 0);
        var footer = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        footer.Children.Add(sameWidth);
        footer.Children.Add(note);

        Grid.SetRow(head, 0);
        Grid.SetRow(scroll, 1);
        Grid.SetRow(footer, 2);
        _root.Children.Add(head);
        _root.Children.Add(scroll);
        _root.Children.Add(footer);
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

        _summary.Text = T("tool.pdforganize.pages", _plan.Count);
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
        actions.Children.Add(PdfToolChrome.IconButton("\uE74D", T("tool.pdforganize.delete"), () =>
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
        var sameWidth = _sameWidth;
        var directory = PdfToolOutput.DirectoryFor(_item, _targets);
        return await OutputPaths.ProduceAsync(directory, $"{_item.BaseName} {T("tool.pdforganize.suffix")}", "pdf",
            temporary => Task.Run(() => PdfPageWriter.Write(_item.Path, pages, temporary, sameWidth)));
    }
}

/// <summary>
/// Writes an ordered page list from one PDF: the source is opened read-only once and every entry imports
/// its page again, so a duplicated page becomes an independent page in the result. The source file is
/// never modified. With <c>sameWidth</c> every page's content and boxes are scaled by one factor so the
/// visual width matches the narrowest page; the heights keep that same factor and can still differ.
/// </summary>
internal static class PdfPageWriter
{
    public static void Write(string sourcePath, IReadOnlyList<PdfPageRef> pages, string output, bool sameWidth)
    {
        PdfDocument source;
        try
        {
            source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is not ConversionException and not OutOfMemoryException)
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

            if (sameWidth)
                ScaleToNarrowest(result);

            result.Save(output);
        }
    }

    /// <summary>Scales every page so its displayed width matches the narrowest page in the document.</summary>
    private static void ScaleToNarrowest(PdfDocument document)
    {
        var boxes = new List<PdfPageBox>(document.PageCount);
        var rotations = new List<int>(document.PageCount);
        for (var index = 0; index < document.PageCount; index++)
        {
            boxes.Add(BoxOf(document.Pages[index]));
            rotations.Add(document.Pages[index].Rotate);
        }

        var widths = boxes.Select((box, index) => PdfOrganize.VisualWidth(box, rotations[index])).ToList();
        var target = widths.Where(width => width > 0).DefaultIfEmpty(0).Min();
        if (target <= 0)
            return;

        for (var index = 0; index < document.PageCount; index++)
        {
            var scale = PdfOrganize.ScaleToWidth(boxes[index], rotations[index], target);
            if (scale <= 0 || Math.Abs(scale - 1) < 0.0001)
                continue;
            ScalePage(document.Pages[index], scale);
        }
    }

    /// <summary>The box a viewer shows: the crop box, or the media box when no crop box is set.</summary>
    private static PdfPageBox BoxOf(PdfPage page)
    {
        var crop = page.EffectiveCropBoxReadOnly;
        if (crop.Width > 0 && crop.Height > 0)
            return new PdfPageBox(crop.Width, crop.Height);
        return new PdfPageBox(page.Width.Point, page.Height.Point);
    }

    /// <summary>
    /// Wraps the existing content in <c>q … cm … Q</c> so it draws at <paramref name="scale"/> of its old
    /// size, then moves every page box by the same factor so viewers and printers agree.
    /// </summary>
    private static void ScalePage(PdfPage page, double scale)
    {
        var factor = scale.ToString("0.######", CultureInfo.InvariantCulture);
        var prefix = page.Contents.PrependContent();
        prefix.CreateStream(Encoding.ASCII.GetBytes($"q {factor} 0 0 {factor} 0 0 cm\n"));
        var suffix = page.Contents.AppendContent();
        suffix.CreateStream(Encoding.ASCII.GetBytes("\nQ\n"));

        if (page.HasMediaBox)
            page.MediaBox = Scaled(page.MediaBox, scale);
        if (page.HasCropBox)
            page.CropBox = Scaled(page.CropBox, scale);
        if (!page.HasMediaBox && !page.HasCropBox)
        {
            page.Width = XUnit.FromPoint(page.Width.Point * scale);
            page.Height = XUnit.FromPoint(page.Height.Point * scale);
        }
        if (page.HasTrimBox)
            page.TrimBox = Scaled(page.TrimBox, scale);
        if (page.HasArtBox)
            page.ArtBox = Scaled(page.ArtBox, scale);
        if (page.HasBleedBox)
            page.BleedBox = Scaled(page.BleedBox, scale);
    }

    private static PdfRectangle Scaled(PdfRectangle box, double scale) =>
        new(new XRect(box.X1 * scale, box.Y1 * scale, box.Width * scale, box.Height * scale));
}
