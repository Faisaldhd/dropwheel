using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;
using Rectangle = System.Windows.Shapes.Rectangle;
using WpfImage = System.Windows.Controls.Image;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Crop a single image: the preview shows the picture with a crop rectangle (corner and edge handles,
/// dimmed outside), aspect-ratio chips, and W/H boxes in original pixels. Apply writes a new file next to
/// the source (or into the drop folder) and never touches the original.
/// </summary>
internal sealed class CropTool : IToolWindowSpec
{
    public static readonly CropTool Instance = new();

    private static readonly HashSet<string> Formats =
        new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp", "avif", "tiff", "bmp", "ico", "gif" };

    public string Id => "tools.crop";

    public string TitleKey => "tool.crop.title";

    public (double Width, double Height) Sizes => ToolSizes.Crop;

    public bool Applies(IReadOnlyList<FileItem> items) =>
        items.Count == 1 && !items[0].IsDirectory && Formats.Contains(items[0].Format);

    public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new CropView(items[0], targets).Build();
}

/// <summary>Registers the tool from its own file, so no shared list has to be edited.</summary>
internal static class CropToolRegistration
{
    [ModuleInitializer]
    internal static void Register() => ToolRegistry.Register(CropTool.Instance);
}

/// <summary>The crop window content: preview, rectangle handles, ratio chips, width/height boxes.</summary>
internal sealed class CropView
{
    private static readonly (string Key, double? Ratio)[] Ratios =
    [
        ("tool.crop.aspect.free", null),
        ("tool.crop.aspect.1x1", 1.0),
        ("tool.crop.aspect.16x9", 16.0 / 9.0),
        ("tool.crop.aspect.9x16", 9.0 / 16.0),
        ("tool.crop.aspect.4x3", 4.0 / 3.0),
        ("tool.crop.aspect.3x4", 3.0 / 4.0),
    ];

    private readonly FileItem _item;
    private readonly DropTargets? _targets;
    private readonly BitmapSource _bitmap;
    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private readonly Grid _preview = new() { Height = 450, Margin = new Thickness(0, 4, 0, 10) };
    private readonly Canvas _overlay = new();
    private readonly Border _frame = new() { BorderBrush = Theme.Accent, BorderThickness = new Thickness(2) };
    private readonly List<(CropHandle Handle, Border Element)> _handles = [];
    private readonly Rectangle _dimTop = new();
    private readonly Rectangle _dimBottom = new();
    private readonly Rectangle _dimLeft = new();
    private readonly Rectangle _dimRight = new();
    private readonly StackPanel _chipRow = new() { Orientation = System.Windows.Controls.Orientation.Horizontal };
    private readonly TextBox _widthBox = new() { Width = 72 };
    private readonly TextBox _heightBox = new() { Width = 72 };
    private readonly Dictionary<CropHandle, Point> _dragOrigin = [];
    private readonly Dictionary<CropHandle, CropRect> _dragStart = [];

    private CropRect _rect;
    private double? _ratio;
    private double _scale = 1;
    private double _originX;
    private double _originY;
    private bool _syncing;

    public CropView(FileItem item, DropTargets? targets)
    {
        _item = item;
        _targets = targets;
        (_bitmap, _imageWidth, _imageHeight) = Load(item.Path);
        _rect = CropMath.Full(_imageWidth, _imageHeight);
    }

    public ToolContent Build()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var preview = BuildPreview();
        var chips = BuildChips();
        var boxes = BuildBoxes();
        Grid.SetRow(preview, 0);
        Grid.SetRow(chips, 1);
        Grid.SetRow(boxes, 2);
        root.Children.Add(preview);
        root.Children.Add(chips);
        root.Children.Add(boxes);

        Reset();
        return new ToolContent(root, Reset, ApplyAsync);
    }

    private FrameworkElement BuildPreview()
    {
        var image = new WpfImage { Source = _bitmap, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        var dim = Brushes.Black;
        foreach (var rect in new[] { _dimTop, _dimBottom, _dimLeft, _dimRight })
        {
            rect.Fill = dim;
            rect.Opacity = 0.55;
            _overlay.Children.Add(rect);
        }
        _overlay.Children.Add(_frame);

        foreach (var handle in Enum.GetValues<CropHandle>().Where(h => h != CropHandle.None))
        {
            var element = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = Theme.Accent,
                BorderBrush = Theme.TextOnAccent,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeAll,
            };
            element.MouseLeftButtonDown += (s, e) => BeginDrag(handle, (FrameworkElement)s, e);
            element.MouseMove += (_, e) => ContinueDrag(handle, e);
            element.MouseLeftButtonUp += (_, _) => EndDrag((FrameworkElement)element);
            _handles.Add((handle, element));
            _overlay.Children.Add(element);
        }

        _preview.Children.Add(image);
        _preview.Children.Add(_overlay);
        _preview.SizeChanged += (_, _) => Layout();
        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = _preview,
        };
    }

    private FrameworkElement BuildChips()
    {
        _chipRow.Margin = new Thickness(0, 0, 0, 10);
        foreach (var (key, ratio) in Ratios)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = Theme.Track,
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = T(key), FontSize = 12, Foreground = Theme.Text },
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _ratio = ratio;
                if (ratio is double value)
                    _rect = CropMath.SnapToRatio(_rect, _imageWidth, _imageHeight, value);
                SyncBoxes();
                Layout();
                PaintChips();
            };
            _chipRow.Children.Add(chip);
        }
        return _chipRow;
    }

    private FrameworkElement BuildBoxes()
    {
        foreach (var box in new[] { _widthBox, _heightBox })
        {
            box.Padding = new Thickness(8, 6, 8, 6);
            box.Background = Theme.Card;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.PanelBorder;
            box.BorderThickness = new Thickness(1);
            box.HorizontalContentAlignment = HorizontalAlignment.Center;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                    CommitBoxes();
            };
            box.LostFocus += (_, _) => CommitBoxes();
        }

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = T("tool.crop.width"), Foreground = Theme.Muted, Margin = new Thickness(0, 8, 8, 0), FontSize = 12 });
        row.Children.Add(_widthBox);
        row.Children.Add(new TextBlock { Text = T("tool.crop.height"), Foreground = Theme.Muted, Margin = new Thickness(18, 8, 8, 0), FontSize = 12 });
        row.Children.Add(_heightBox);
        return row;
    }

    private void PaintChips()
    {
        for (var i = 0; i < _chipRow.Children.Count && i < Ratios.Length; i++)
        {
            var selected = Equals(Ratios[i].Ratio, _ratio);
            if (_chipRow.Children[i] is Border chip)
            {
                chip.Background = selected ? Theme.Accent : Theme.Track;
                            if (chip.Child is TextBlock chipText)
                    chipText.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
            }
        }
    }

    private void Reset()
    {
        _ratio = null;
        _rect = CropMath.Full(_imageWidth, _imageHeight);
        SyncBoxes();
        Layout();
        PaintChips();
    }

    private void BeginDrag(CropHandle handle, FrameworkElement element, MouseButtonEventArgs e)
    {
        _dragOrigin[handle] = e.GetPosition(_preview);
        _dragStart[handle] = _rect;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ContinueDrag(CropHandle handle, MouseEventArgs e)
    {
        if (!_dragStart.TryGetValue(handle, out var start) || !_dragOrigin.TryGetValue(handle, out var origin))
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(_preview);
        var dx = (point.X - origin.X) / _scale;
        var dy = (point.Y - origin.Y) / _scale;
        _rect = CropMath.Drag(handle, start, dx, dy, _imageWidth, _imageHeight, _ratio);
        SyncBoxes();
        Layout();
    }

    private void EndDrag(FrameworkElement element) => element.ReleaseMouseCapture();

    private void CommitBoxes()
    {
        if (_syncing)
            return;
        if (!int.TryParse(_widthBox.Text, out var width) || !int.TryParse(_heightBox.Text, out var height))
        {
            SyncBoxes();
            return;
        }
        _rect = CropMath.FromPixels(_rect.X, _rect.Y, width, height, _imageWidth, _imageHeight);
        SyncBoxes();
        Layout();
    }

    private void SyncBoxes()
    {
        var (width, height) = CropMath.Pixels(_rect);
        _syncing = true;
        _widthBox.Text = width.ToString();
        _heightBox.Text = height.ToString();
        _syncing = false;
    }

    private void Layout()
    {
        var availableWidth = Math.Max(1, _preview.ActualWidth - 12);
        var availableHeight = Math.Max(1, _preview.ActualHeight - 12);
        _scale = Math.Min(availableWidth / _imageWidth, availableHeight / _imageHeight);
        if (_scale <= 0 || double.IsInfinity(_scale))
            _scale = 1;
        var displayWidth = _imageWidth * _scale;
        var displayHeight = _imageHeight * _scale;
        _originX = (_preview.ActualWidth - displayWidth) / 2;
        _originY = (_preview.ActualHeight - displayHeight) / 2;

        var left = _originX + _rect.X * _scale;
        var top = _originY + _rect.Y * _scale;
        var width = _rect.Width * _scale;
        var height = _rect.Height * _scale;

        Canvas.SetLeft(_frame, left);
        Canvas.SetTop(_frame, top);
        _frame.Width = width;
        _frame.Height = height;

        Dim(_dimTop, _originX, _originY, displayWidth, top - _originY);
        Dim(_dimBottom, _originX, top + height, displayWidth, _originY + displayHeight - (top + height));
        Dim(_dimLeft, _originX, top, left - _originX, height);
        Dim(_dimRight, left + width, top, _originX + displayWidth - (left + width), height);

        foreach (var (handle, element) in _handles)
        {
            var point = HandlePoint(handle, left, top, width, height);
            Canvas.SetLeft(element, point.X - element.Width / 2);
            Canvas.SetTop(element, point.Y - element.Height / 2);
        }
    }

    private static void Dim(Rectangle rectangle, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rectangle, Math.Max(0, left));
        Canvas.SetTop(rectangle, Math.Max(0, top));
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }

    private static Point HandlePoint(CropHandle handle, double left, double top, double width, double height) => handle switch
    {
        CropHandle.TopLeft => new Point(left, top),
        CropHandle.Top => new Point(left + width / 2, top),
        CropHandle.TopRight => new Point(left + width, top),
        CropHandle.Right => new Point(left + width, top + height / 2),
        CropHandle.BottomRight => new Point(left + width, top + height),
        CropHandle.Bottom => new Point(left + width / 2, top + height),
        CropHandle.BottomLeft => new Point(left, top + height),
        CropHandle.Left => new Point(left, top + height / 2),
        _ => new Point(left, top),
    };

    private async Task<string?> ApplyAsync()
    {
        if (!CropTool.Instance.Applies([_item]))
            return null;

        var directory = OutputDirectory();
        var rect = _rect;
        return await Task.Run(() => CropRunner.Apply(_item, rect, directory));
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

    /// <summary>Reads the image, applies its EXIF orientation and keeps the pixels for the preview.</summary>
    private static (BitmapSource Bitmap, int Width, int Height) Load(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        var width = (int)image.Width;
        var height = (int)image.Height;
        var bytes = image.ToByteArray(MagickFormat.Png);
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return (bitmap, width, height);
    }
}

/// <summary>Writes the cropped copy with Magick.NET; the original file is only read.</summary>
internal static class CropRunner
{
    public static string Apply(FileItem item, CropRect rect, string outputDirectory)
    {
        using var image = new MagickImage(item.Path);
        image.AutoOrient();

        var x = Math.Clamp((int)Math.Round(rect.X), 0, Math.Max(0, (int)image.Width - 1));
        var y = Math.Clamp((int)Math.Round(rect.Y), 0, Math.Max(0, (int)image.Height - 1));
        var width = Math.Min(CropMath.Pixels(rect).Width, (int)image.Width - x);
        var height = Math.Min(CropMath.Pixels(rect).Height, (int)image.Height - y);
        image.Crop(new MagickGeometry(x, y, (uint)Math.Max(1, width), (uint)Math.Max(1, height)));

        var extension = Path.GetExtension(item.Path).TrimStart('.');
        var target = OutputPaths.Unique(outputDirectory, $"{item.BaseName} {Loc.T("tool.crop.suffix")}", extension);
        image.Write(target);
        return target;
    }
}
