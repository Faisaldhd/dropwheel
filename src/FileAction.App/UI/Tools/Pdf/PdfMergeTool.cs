using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using PdfDocument = PdfSharp.Pdf.PdfDocument;
using PdfDocumentOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfReader = PdfSharp.Pdf.IO.PdfReader;
using PdfReaderException = PdfSharp.Pdf.IO.PdfReaderException;
using WpfImage = System.Windows.Controls.Image;
using XGraphics = PdfSharp.Drawing.XGraphics;
using XImage = PdfSharp.Drawing.XImage;
using XUnit = PdfSharp.Drawing.XUnit;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Pdf;

/// <summary>
/// "Merge to PDF" on the Tools wheel: every dropped PDF contributes all of its pages and every dropped
/// image contributes exactly one page. The rows are the order the pages will have; drag a row or use its
/// up/down arrows to move it and remove the ones that should not be included.
/// <para>
/// The initial order is the order the app received the files — Explorer's selection order when the shell
/// provides it, which a drag or a context-menu selection normally does. Paths collected without a
/// selection (for example from the clipboard) can instead arrive alphabetically; the up/down arrows and
/// drag exist so the final order is always explicit.
/// </para>
/// </summary>
internal sealed class PdfMergeTool : IToolWindowSpec
{
    public static readonly PdfMergeTool Instance = new();

    /// <summary>About 620 x 700 DIP at the default preview box; the rows scroll inside it.</summary>
    public static readonly ToolSpecLayout Size = new(
        ChromeWidth: 40,
        ChromeHeight: 300,
        PreviewMinWidth: 360,
        PreviewMinHeight: 260,
        PreviewMaxWidth: 900,
        PreviewMaxHeight: 640,
        DefaultPreviewWidth: 580,
        DefaultPreviewHeight: 400);

    public string Id => "tools.pdfmerge";

    public string TitleKey => "tool.pdfmerge.title";

    public string PrimaryLabelKey => "tool.pdfmerge.merge";

    public ToolSpecLayout Layout => Size;

    /// <summary>Two or more PDFs and/or images; a single image is the Crop tool's job.</summary>
    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count >= 2 && items.All(PdfOrganize.CanMerge);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new PdfMergeView(items, targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class PdfMergeToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(PdfMergeTool.Instance);
}

/// <summary>The window content: the ordered rows, their thumbnails, drag-reorder and the remove action.</summary>
internal sealed class PdfMergeView
{
    private const string DragFormat = "DropWheel.PdfMergeRow";
    private const double DragThreshold = 8;
    private const int ThumbWidth = 120;

    private readonly DropTargets? _targets;
    private readonly PdfMergePlan _plan;
    private readonly Dictionary<string, BitmapSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Border Row, WpfImage Image, TextBlock Fallback, string Path)> _rows = [];
    private readonly Grid _root = new();
    private StackPanel _list = new();
    private TextBlock _summary = Summary();

    private Point _dragStart;
    private int _dragFrom = -1;

    private static TextBlock Summary() => new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public PdfMergeView(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _targets = targets;
        _plan = new PdfMergePlan(items.Select(item => new PdfMergeSource(item.Path, PdfOrganize.KindOf(item))));
    }

    public ToolContent Build()
    {
        BuildBody();
        Rebuild();
        // Windows can flip dark/light while the window is open; rebuild so every brush is the new palette.
        PdfThemeWatcher.Subscribe(RepaintForTheme);
        return new ToolContent(_root, Reset, ApplyAsync, Task.FromResult<MediaSize?>(null), ActivateAsync);
    }

    private void RepaintForTheme()
    {
        if (!_root.IsVisible)
            return;
        BuildBody();
        Rebuild();
    }

    private void BuildBody()
    {
        _root.Children.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _summary = Summary();
        _list = new StackPanel();

        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        var title = new TextBlock
        {
            Text = T("tool.pdfmerge.order"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = T("tool.pdfmerge.hint"),
        };
        DockPanel.SetDock(_summary, Dock.Right);
        head.Children.Add(_summary);
        head.Children.Add(title);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list,
            AllowDrop = true,
            Background = Brushes.Transparent,
        };

        // The whole viewport is a drop target, so dropping below the last row moves that row to the end.
        scroll.DragOver += (_, e) =>
        {
            if (DragIndex(e.Data) is not null)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };
        scroll.Drop += (_, e) =>
        {
            if (DragIndex(e.Data) is not int from || _plan.Count == 0)
                return;
            MoveRow(from, _plan.Count - 1);
            e.Handled = true;
        };

        Grid.SetRow(head, 0);
        Grid.SetRow(scroll, 1);
        _root.Children.Add(head);
        _root.Children.Add(scroll);
    }

    /// <summary>Fills in the row thumbnails one by one; the window is usable while they load.</summary>
    public async Task ActivateAsync()
    {
        foreach (var source in _plan.Sources.ToList())
        {
            if (_thumbnails.ContainsKey(source.Path))
                continue;
            BitmapSource? bitmap = null;
            try
            {
                bitmap = await Task.Run(() => source.IsImage
                    ? PdfThumbnails.FromImage(source.Path, ThumbWidth)
                    : PdfThumbnails.FromPdf(source.Path, 0, ThumbWidth));
            }
            catch
            {
                // A damaged file still stays in the list; its row shows why the preview is missing.
                bitmap = null;
            }
            _thumbnails[source.Path] = bitmap;
            PaintThumbnail(source.Path);
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        _rows.Clear();

        if (_plan.IsEmpty)
        {
            _list.Children.Add(new Border
            {
                Margin = new Thickness(0, 24, 0, 0),
                Child = new TextBlock
                {
                    Text = T("tool.pdfmerge.error.plan"),
                    FontSize = 13,
                    Foreground = Theme.Muted,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }
        else
        {
            for (var index = 0; index < _plan.Count; index++)
                _list.Children.Add(BuildRow(_plan[index], index));
        }

        _summary.Text = Loc.Items(_plan.Count);
        _summary.Visibility = _plan.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        foreach (var path in _thumbnails.Keys)
            PaintThumbnail(path);
    }

    private Border BuildRow(PdfMergeSource source, int index)
    {
        var number = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = Theme.AccentSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock
            {
                Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontSize = 11,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var (thumbnail, image, fallback) = PdfToolChrome.Thumbnail(44, 58);

        // The small type icon says PDF or image at a glance; the tile above is the page preview.
        var icon = new TextBlock
        {
            Text = source.IsImage ? Glyphs.Photo : Glyphs.Document,
            FontFamily = Theme.Icons,
            FontSize = 14,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
        };

        var name = new TextBlock
        {
            Text = source.FileName,
            FontSize = 13,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var up = PdfToolChrome.IconButton("\uE70E", T("tool.pdfmerge.moveUp"), () =>
        {
            _plan.MoveUp(index);
            Rebuild();
        }, enabled: _plan.CanMoveUp(index));
        var down = PdfToolChrome.IconButton("\uE70D", T("tool.pdfmerge.moveDown"), () =>
        {
            _plan.MoveDown(index);
            Rebuild();
        }, enabled: _plan.CanMoveDown(index));
        var remove = PdfToolChrome.IconButton("\uE711", T("tool.pdfmerge.remove"), () =>
        {
            _plan.RemoveAt(index);
            Rebuild();
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(number, 0);
        Grid.SetColumn(thumbnail, 1);
        Grid.SetColumn(icon, 2);
        Grid.SetColumn(name, 3);
        Grid.SetColumn(up, 4);
        Grid.SetColumn(down, 5);
        Grid.SetColumn(remove, 6);
        up.VerticalAlignment = VerticalAlignment.Center;
        down.VerticalAlignment = VerticalAlignment.Center;
        remove.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(number);
        grid.Children.Add(thumbnail);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(up);
        grid.Children.Add(down);
        grid.Children.Add(remove);

        var row = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
            Tag = source.Path,
            AllowDrop = true,
        };

        // Drag the row to a new position; dropping on the empty area below moves it to the end.
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(row);
            _dragFrom = index;
        };
        row.MouseLeftButtonUp += (_, _) => _dragFrom = -1;
        row.MouseMove += (_, e) =>
        {
            if (_dragFrom != index || e.LeftButton != MouseButtonState.Pressed)
                return;
            if (Math.Abs(e.GetPosition(row).Y - _dragStart.Y) < DragThreshold)
                return;
            var data = new DataObject(DragFormat, index);
            _dragFrom = -1;
            DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
        };
        row.DragOver += (_, e) =>
        {
            if (DragIndex(e.Data) is null)
                return;
            row.BorderBrush = Theme.Accent;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        };
        row.DragLeave += (_, _) => row.BorderBrush = Theme.PanelBorder;
        row.Drop += (_, e) =>
        {
            if (DragIndex(e.Data) is not int from)
                return;
            MoveRow(from, index);
            e.Handled = true;
        };

        _rows.Add((row, image, fallback, source.Path));
        return row;
    }

    private void MoveRow(int from, int to)
    {
        if (_plan.Move(from, to))
            Rebuild();
    }

    private static int? DragIndex(IDataObject data) =>
        data.GetDataPresent(DragFormat) && data.GetData(DragFormat) is int value ? value : null;

    private void PaintThumbnail(string path)
    {
        foreach (var (_, image, fallback, rowPath) in _rows)
        {
            if (!string.Equals(rowPath, path, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_thumbnails.TryGetValue(path, out var bitmap) && bitmap is not null)
            {
                image.Source = bitmap;
                image.Visibility = Visibility.Visible;
                fallback.Visibility = Visibility.Collapsed;
            }
            else if (_thumbnails.ContainsKey(path))
            {
                image.Visibility = Visibility.Collapsed;
                fallback.Visibility = Visibility.Visible;
                // The row tile is only 44 x 58: the full "Preview unavailable" was clipped to "ew unava".
                PdfToolChrome.Fail(fallback, "tool.pdfmerge.thumbFailedShort", 44);
            }
        }
    }

    private void Reset()
    {
        _plan.Reset();
        Rebuild();
    }

    private async Task<string?> ApplyAsync()
    {
        if (_plan.IsEmpty)
            return null;
        if (!_plan.Validate().IsValid)
            throw new ConversionException(T("tool.pdfmerge.error.plan"));

        var sources = _plan.Sources.ToList();
        var directory = PdfToolOutput.DirectoryFor(new FileItem(sources[0].Path), _targets);
        return await OutputPaths.ProduceAsync(directory, T("tool.pdfmerge.suffix"), "pdf",
            temporary => Task.Run(() => PdfMergeWriter.Write(sources, temporary)));
    }
}

/// <summary>
/// Writes the merge plan: every PDF contributes all its pages in file order, every image becomes one page
/// sized from its pixels (96 DPI â†’ points). The sources are only read; the output is a new file.
/// </summary>
internal static class PdfMergeWriter
{
    public static void Write(IReadOnlyList<PdfMergeSource> sources, string output)
    {
        using var document = new PdfDocument();
        var images = new List<(XImage Image, MemoryStream Stream)>();
        try
        {
            foreach (var source in sources)
            {
                if (source.IsImage)
                    AppendImagePage(document, source.Path, images);
                else
                    AppendPdfPages(document, source.Path);
            }
            document.Save(output);
        }
        finally
        {
            foreach (var (image, stream) in images)
            {
                image.Dispose();
                stream.Dispose();
            }
        }
    }

    private static void AppendPdfPages(PdfDocument output, string path)
    {
        try
        {
            using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (var index = 0; index < input.PageCount; index++)
                output.AddPage(input.Pages[index]);
        }
        catch (Exception ex) when (ex is not ConversionException and not OutOfMemoryException)
        {
            throw new ConversionException(T("tool.pdfmerge.error.pdf", Path.GetFileName(path)), ex);
        }
    }

    private static void AppendImagePage(PdfDocument output, string path, List<(XImage Image, MemoryStream Stream)> keep)
    {
        byte[] png;
        int pixelWidth;
        int pixelHeight;
        try
        {
            using var image = new ImageMagick.MagickImage(path);
            image.AutoOrient();
            pixelWidth = (int)image.Width;
            pixelHeight = (int)image.Height;
            if (pixelWidth <= 0 || pixelHeight <= 0)
                throw new InvalidOperationException("The image has no pixels.");

            // Magick's default encoder may write an indexed/palette PNG for a flat or few-colour
            // source, and PDFsharp's XImage decoder rejects that as "Unsupported image format".
            // Normalize to an 8-bit true-colour (24-bit RGB) PNG, compositing transparency on white.
            image.BackgroundColor = ImageMagick.MagickColors.White;
            image.Alpha(ImageMagick.AlphaAction.Remove);
            image.ColorType = ImageMagick.ColorType.TrueColor;
            image.Depth = 8;
            png = image.ToByteArray(ImageMagick.MagickFormat.Png24);
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw new ConversionException(T("tool.pdfmerge.error.image", Path.GetFileName(path)), ex);
        }

        // A 96 DPI pixel maps to 0.75 pt, so a screenshot keeps its on-screen physical size.
        var width = pixelWidth * 72.0 / 96.0;
        var height = pixelHeight * 72.0 / 96.0;

        var stream = new MemoryStream(png);
        XImage ximage;
        try
        {
            ximage = XImage.FromStream(stream);
        }
        catch (Exception ex)
        {
            stream.Dispose();
            throw new ConversionException(T("tool.pdfmerge.error.image", Path.GetFileName(path)), ex);
        }

        keep.Add((ximage, stream));
        var page = output.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(height);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(ximage, 0, 0, width, height);
    }
}
