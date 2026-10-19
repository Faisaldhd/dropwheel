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
/// image contributes exactly one page. The rows are the order the pages will have; drag a row to move it
/// and remove the ones that should not be included.
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
    private readonly StackPanel _list = new();
    private readonly TextBlock _summary = new()
    {
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Point _dragStart;
    private int _dragFrom = -1;

    public PdfMergeView(IReadOnlyList<FileItem> items, DropTargets? targets)
    {
        _targets = targets;
        _plan = new PdfMergePlan(items.Select(item => new PdfMergeSource(item.Path, PdfOrganize.KindOf(item))));
    }

    public ToolContent Build()
    {
        var head = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 10) };
        var hint = Controls.Text(T("tool.pdfmerge.hint"), 12, brush: Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_summary, Dock.Right);
        head.Children.Add(_summary);
        head.Children.Add(hint);

        _list.AllowDrop = true;
        _list.Background = Brushes.Transparent;
        _list.DragOver += (_, e) =>
        {
            if (DragIndex(e.Data) is not null)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        };
        _list.Drop += (_, e) =>
        {
            if (DragIndex(e.Data) is not int from || _plan.Count == 0)
                return;
            MoveRow(from, _plan.Count - 1);
            e.Handled = true;
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _list,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(head, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(head);
        root.Children.Add(scroll);

        Rebuild();
        return new ToolContent(root, Reset, ApplyAsync, Task.FromResult<MediaSize?>(null), ActivateAsync);
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

        var (thumbnail, image, fallback) = PdfToolChrome.Thumbnail(50, 66);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        text.Children.Add(new TextBlock
        {
            Text = source.FileName,
            FontSize = 13,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = Formats.Display(Formats.KeyOf(source.Path)),
            FontSize = 11,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var remove = PdfToolChrome.IconButton("\uE711", T("tool.pdfmerge.remove"), () =>
        {
            _plan.RemoveAt(index);
            Rebuild();
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(number, 0);
        Grid.SetColumn(thumbnail, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(remove, 3);
        remove.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(number);
        grid.Children.Add(thumbnail);
        grid.Children.Add(text);
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
                fallback.Text = T("tool.pdfmerge.thumbFailed");
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
        catch (Exception ex) when (ex is PdfReaderException or IOException or UnauthorizedAccessException or InvalidOperationException)
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
            png = image.ToByteArray(ImageMagick.MagickFormat.Png);
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
