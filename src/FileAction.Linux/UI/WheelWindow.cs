using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileAction.Core;
using static FileAction.Core.Loc;
using Path = Avalonia.Controls.Shapes.Path;

namespace FileAction.Linux.UI;

/// <summary>
/// The radial menu. Opened for files (from the file manager or the command line) it works with mouse and
/// keyboard; opened empty it is a drop zone that stays on screen and offers what fits whatever you drop on it.
/// </summary>
public sealed class WheelWindow : Window
{
    public delegate IReadOnlyList<Operation> OperationSource(WheelKind wheel, IReadOnlyList<FileItem> items);

    private const double OuterRadius = 196;
    private const double InnerRadius = 80;
    private const double Rim = 9;
    private const double Gap = 6;
    private const double Corner = 11;
    private const double PopOut = 7;
    private const double Pad = 16;
    private const double Size = 2 * (OuterRadius + Rim + Pad);
    private static readonly WheelKind[] WheelOrder = [WheelKind.Convert, WheelKind.Tools, WheelKind.Actions];

    private readonly OperationSource _source;
    private readonly Action<Operation, IReadOnlyList<FileItem>, bool> _execute;
    private readonly bool _dropZone;
    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<Path> _slices = [];
    private readonly List<StackPanel> _labels = [];
    private readonly List<TranslateTransform> _moves = [];
    private IReadOnlyList<FileItem> _items;
    private IReadOnlyList<Operation> _ops = [];
    private WheelKind _wheel = WheelKind.Convert;
    private int _highlight = -1;
    private bool _repeat;
    private bool _dragging;

    /// <param name="execute">Runs an operation; the flag says it is a "repeat last" run.</param>
    public WheelWindow(IReadOnlyList<FileItem>? items, OperationSource source, Action<Operation, IReadOnlyList<FileItem>, bool> execute)
    {
        _items = items ?? [];
        _dropZone = _items.Count == 0;
        _source = source;
        _execute = execute;

        Title = "DropWheel";
        Width = Size;
        Height = Size;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = _dropZone;
        Topmost = true;
        Content = _canvas;
        Icon = AppIcon.Window;

        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragEnterHandler(this, OnDragOver);
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDragLeaveHandler(this, (_, _) => OnDragLeave());
        DragDrop.AddDropHandler(this, OnDrop);
        PointerMoved += (_, e) =>
        {
            if (!_dragging)
                SetHighlight(HitTest(e.GetPosition(_canvas), out bool _));
        };
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
        Opened += (_, _) => PlaceAtPointer();

        Load();
        Build();
    }

    /// <summary>The last operation if it fits these items (for "repeat last").</summary>
    public Func<IReadOnlyList<FileItem>, Operation?>? LastOperation { get; init; }

    public WheelKind CurrentWheel => _wheel;

    private void PlaceAtPointer()
    {
        if (Shell.X11.Pointer() is not { } p)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        var half = (int)(Size * RenderScaling / 2);
        var screen = Screens.ScreenFromPoint(new PixelPoint(p.X, p.Y))?.WorkingArea;
        var x = p.X - half;
        var y = p.Y - half;
        if (screen is { } area)
        {
            x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - 2 * half));
            y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - 2 * half));
        }
        Position = new PixelPoint(x, y);
    }

    public void SwitchTo(WheelKind wheel)
    {
        _wheel = wheel;
        _repeat = false;
        Load();
        Build();
        if (!_dropZone && _ops.Count > 0)
            SetHighlight(0);
    }

    public void ToggleRepeat()
    {
        if (_repeat)
        {
            SwitchTo(_wheel);
            return;
        }
        _repeat = true;
        Load();
        Build();
        if (!_dropZone && _ops.Count > 0)
            SetHighlight(0);
    }

    private void Load()
    {
        _ops = _items.Count == 0 ? []
            : _repeat ? (LastOperation?.Invoke(_items) is { } last ? [last] : [])
            : _source(_wheel, _items);
        _highlight = -1;
    }

    // ------------------------------------------------------------------ input

    private static List<FileItem>? FilesOf(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles() is { Length: > 0 } files
            ? files.Select(f => f.TryGetLocalPath()).OfType<string>().Select(p => new FileItem(p)).ToList()
            : null;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!_dragging)
        {
            _dragging = true;
            if (FilesOf(e) is { Count: > 0 } items)
            {
                _items = items;
                Load();
                Build();
            }
        }
        var index = HitTest(e.GetPosition(_canvas), out _);
        SetHighlight(index);
        e.DragEffects = index >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragLeave()
    {
        _dragging = false;
        SetHighlight(-1);
        if (_dropZone)
        {
            _items = [];
            Load();
            Build();
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var index = HitTest(e.GetPosition(_canvas), out _);
        var items = FilesOf(e) ?? [.. _items];
        _dragging = false;
        if (index >= 0 && index < _ops.Count && items.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            _execute(_ops[index], items, _repeat);
        }
        if (_dropZone)
            OnDragLeave();
        else
            Close();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HitTest(e.GetPosition(_canvas), out var inHub);
        // The drop zone can be moved by its middle.
        if (inHub && _dropZone && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        var index = HitTest(e.GetPosition(_canvas), out var inHub);
        if (index >= 0)
            Run(index);
        else if (inHub && !_dropZone)
            Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left or Key.Up:
                Move(-1);
                break;
            case Key.Right or Key.Down:
                Move(1);
                break;
            case Key.Enter or Key.Space:
                if (_highlight >= 0)
                    Run(_highlight);
                break;
            case Key.Tab:
                SwitchTo(WheelOrder[(Array.IndexOf(WheelOrder, _wheel) + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 2 : 1)) % WheelOrder.Length]);
                break;
            case Key.R:
                ToggleRepeat();
                break;
            case >= Key.D1 and <= Key.D9:
                if (e.Key - Key.D1 < _ops.Count)
                    Run(e.Key - Key.D1);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_ops.Count > 0)
            SetHighlight(_highlight < 0 ? 0 : ((_highlight + delta) % _ops.Count + _ops.Count) % _ops.Count);
    }

    private void Run(int index)
    {
        if (_items.Count == 0)
            return;
        var op = _ops[index];
        var items = _items;
        if (!_dropZone)
            Close();
        _execute(op, items, _repeat);
    }

    // ------------------------------------------------------------------ geometry

    private static Point Polar(double radius, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        return new Point(Size / 2 + radius * Math.Sin(rad), Size / 2 - radius * Math.Cos(rad));
    }

    private static double Deg(double radians) => radians * 180 / Math.PI;

    private int HitTest(Point p, out bool inHub)
    {
        var dx = p.X - Size / 2;
        var dy = p.Y - Size / 2;
        var r = Math.Sqrt(dx * dx + dy * dy);
        inHub = r < InnerRadius;
        if (inHub || r > OuterRadius + Rim || _ops.Count == 0)
            return -1;
        var angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        var step = 360.0 / _ops.Count;
        return (int)(((angle + step / 2) % 360) / step) % _ops.Count;
    }

    /// <summary>A ring segment with constant-width gaps and rounded corners (same as on Windows).</summary>
    private static Geometry Sector(double a0, double a1)
    {
        double Edge(double r, double a, int side) => a + side * Deg(Math.Asin(Gap / 2 / r));
        static double Rad(double degrees) => degrees * Math.PI / 180;

        var outerStart = Edge(OuterRadius, a0, 1);
        var outerEnd = Edge(OuterRadius, a1, -1);
        var innerStart = Edge(InnerRadius, a0, 1);
        var innerEnd = Edge(InnerRadius, a1, -1);
        var cOut = Math.Min(Corner, Rad(outerEnd - outerStart) * OuterRadius * 0.45);
        var cIn = Math.Min(Corner * 0.7, Rad(innerEnd - innerStart) * InnerRadius * 0.45);
        var dOut = Deg(cOut / OuterRadius);
        var dIn = Deg(cIn / InnerRadius);

        var start = Polar(OuterRadius, outerStart + dOut);
        var figure = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true, Segments = [] };
        figure.Segments.Add(new ArcSegment
        {
            Point = Polar(OuterRadius, outerEnd - dOut),
            Size = new Avalonia.Size(OuterRadius, OuterRadius),
            IsLargeArc = outerEnd - outerStart - 2 * dOut > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = Polar(OuterRadius, outerEnd), Point2 = Polar(OuterRadius - cOut, Edge(OuterRadius - cOut, a1, -1)) });
        figure.Segments.Add(new LineSegment { Point = Polar(InnerRadius + cIn, Edge(InnerRadius + cIn, a1, -1)) });
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = Polar(InnerRadius, innerEnd), Point2 = Polar(InnerRadius, innerEnd - dIn) });
        figure.Segments.Add(new ArcSegment
        {
            Point = Polar(InnerRadius, innerStart + dIn),
            Size = new Avalonia.Size(InnerRadius, InnerRadius),
            IsLargeArc = innerEnd - innerStart - 2 * dIn > 180,
            SweepDirection = SweepDirection.CounterClockwise,
        });
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = Polar(InnerRadius, innerStart), Point2 = Polar(InnerRadius + cIn, Edge(InnerRadius + cIn, a0, 1)) });
        figure.Segments.Add(new LineSegment { Point = Polar(OuterRadius - cOut, Edge(OuterRadius - cOut, a0, 1)) });
        figure.Segments.Add(new QuadraticBezierSegment { Point1 = Polar(OuterRadius, outerStart), Point2 = start });
        return new PathGeometry { Figures = [figure] };
    }

    private static Geometry Ring()
    {
        var c = new Point(Size / 2, Size / 2);
        return new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry { Center = c, RadiusX = OuterRadius, RadiusY = OuterRadius },
            new EllipseGeometry { Center = c, RadiusX = InnerRadius, RadiusY = InnerRadius });
    }

    // ------------------------------------------------------------------ visuals

    private static void Place(Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
    }

    private void Build()
    {
        Palette.Refresh();
        _canvas.Children.Clear();
        _slices.Clear();
        _labels.Clear();
        _moves.Clear();

        var disc = OuterRadius + Rim;
        var backdrop = new Ellipse
        {
            Width = 2 * disc,
            Height = 2 * disc,
            Fill = Palette.Backdrop,
            Stroke = Palette.BackdropEdge,
            StrokeThickness = 1,
            Effect = Palette.Shadow(28),
        };
        Place(backdrop, Pad, Pad);
        _canvas.Children.Add(backdrop);

        if (_ops.Count == 0 && _items.Count == 0)
        {
            // Empty drop zone: a dashed ring shows where to drop.
            var r = (InnerRadius + OuterRadius) / 2;
            var ring = new Ellipse
            {
                Width = 2 * r,
                Height = 2 * r,
                Stroke = Palette.Muted,
                StrokeThickness = 2,
                StrokeDashArray = [4, 4],
                Opacity = 0.5,
            };
            Place(ring, Size / 2 - r, Size / 2 - r);
            _canvas.Children.Add(ring);
        }

        var n = _ops.Count;
        var step = n == 0 ? 360 : 360.0 / n;
        var labelRadius = InnerRadius + (OuterRadius - InnerRadius) * 0.54;
        var labelWidth = n <= 2 ? 120 : Math.Clamp(2 * labelRadius * Math.Sin(step / 2 * Math.PI / 180) * 0.84, 48, 110);
        for (var i = 0; i < n; i++)
        {
            var a0 = i * step - step / 2;
            var path = new Path
            {
                Data = n == 1 ? Ring() : Sector(a0, a0 + step),
                Fill = Palette.Slice,
                Stroke = Palette.SliceEdge,
                StrokeThickness = 1,
            };
            var label = SliceLabel(_ops[i]);
            var fit = new Viewbox
            {
                Child = label,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = labelWidth,
                MaxHeight = (OuterRadius - InnerRadius) * 0.8,
                IsHitTestVisible = false,
            };
            var mid = n == 1 ? new Point(Size / 2, Size / 2 - labelRadius) : Polar(labelRadius, i * step);
            fit.Measure(Avalonia.Size.Infinity);
            Place(fit, mid.X - fit.DesiredSize.Width / 2, mid.Y - fit.DesiredSize.Height / 2);

            var move = new TranslateTransform
            {
                Transitions =
                [
                    new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(130), Easing = new CubicEaseOut() },
                    new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(130), Easing = new CubicEaseOut() },
                ],
            };
            var group = new Canvas { Width = Size, Height = Size, RenderTransform = move };
            group.Children.Add(path);
            group.Children.Add(fit);
            _slices.Add(path);
            _labels.Add(label);
            _moves.Add(move);
            _canvas.Children.Add(group);
        }

        var hubSize = 2 * (InnerRadius - 6);
        var hub = new Ellipse { Width = hubSize, Height = hubSize, Fill = Palette.Hub, Stroke = Palette.PanelBorder, StrokeThickness = 1, Effect = Palette.Shadow(12) };
        Place(hub, Size / 2 - hubSize / 2, Size / 2 - hubSize / 2);
        _canvas.Children.Add(hub);

        var content = HubContent();
        content.Measure(Avalonia.Size.Infinity);
        Place(content, Size / 2 - content.DesiredSize.Width / 2, Size / 2 - content.DesiredSize.Height / 2);
        _canvas.Children.Add(content);
    }

    private static bool IsFormat(Operation op) => op.Wheel == WheelKind.Convert && op.Label.Length <= 6 && op.Label.ToUpperInvariant() == op.Label;

    private static StackPanel SliceLabel(Operation op)
    {
        var panel = new StackPanel { MaxWidth = 96, IsHitTestVisible = false };
        panel.Children.Add(new TextBlock
        {
            Text = op.Label,
            FontSize = IsFormat(op) ? 19 : 13.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(op.Subtitle))
        {
            panel.Children.Add(new TextBlock
            {
                Text = op.Subtitle,
                Tag = "subtitle",
                FontSize = 10.5,
                Foreground = Palette.Muted,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }
        return panel;
    }

    private readonly TextBlock _title = new() { FontSize = 16, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InnerRadius * 1.65 };
    private readonly TextBlock _detail = new() { FontSize = 11.5, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = InnerRadius * 1.65 };

    private StackPanel HubContent()
    {
        var panel = new StackPanel { Width = InnerRadius * 1.75 };
        panel.Children.Add(HubBadge());
        _title.Foreground = Palette.Text;
        _detail.Foreground = Palette.Muted;
        (_title.Parent as Panel)?.Children.Remove(_title);
        (_detail.Parent as Panel)?.Children.Remove(_detail);
        panel.Children.Add(_title);
        panel.Children.Add(_detail);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var wheel in WheelOrder)
        {
            var active = wheel == _wheel && !_repeat;
            var tab = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(5, 1, 5, 2),
                Background = active ? Palette.AccentGradient : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = WheelName(wheel),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = active ? Palette.TextOnAccent : Palette.Muted,
                },
            };
            var target = wheel;
            tab.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                SwitchTo(target);
            };
            tabs.Children.Add(tab);
        }
        panel.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2),
            Background = Palette.Track,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            Child = tabs,
        });
        UpdateHubText();
        return panel;
    }

    /// <summary>The file type as a small pill, or an arrow (waiting for a drop) / repeat sign.</summary>
    private Control HubBadge()
    {
        if (_items.Count > 0 && !_repeat)
        {
            var format = _items[0].IsDirectory ? T("SLOŽKA", "FOLDER") : Formats.Display(_items[0].Format);
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 0, 0, 6),
                Background = Palette.AccentGradient,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = format.Length == 0 ? "?" : format, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Palette.TextOnAccent },
            };
        }
        var data = _repeat
            ? "M19 12 A7 7 0 1 1 16.95 7.05 M19 4 L19 9 L14 9"   // circular arrow
            : "M12 4 L12 17 M6 11 L12 17 L18 11";                 // arrow down
        return new Path
        {
            Data = Geometry.Parse(data),
            Stroke = Palette.Accent,
            StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = 24,
            Height = 24,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
    }

    public static string WheelName(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => T("Převést", "Convert"),
        WheelKind.Tools => T("Nástroje", "Tools"),
        _ => T("Akce", "Actions"),
    };

    private void SetHighlight(int index)
    {
        if (index == _highlight)
            return;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, false);
        _highlight = index;
        if (_highlight >= 0 && _highlight < _slices.Count)
            Paint(_highlight, true);
        UpdateHubText();
    }

    private void Paint(int index, bool on)
    {
        var slice = _slices[index];
        slice.Fill = on ? Palette.AccentGradient : Palette.Slice;
        slice.Stroke = on ? Brushes.Transparent : Palette.SliceEdge;
        slice.Effect = on ? Palette.Glow() : null;
        foreach (var child in _labels[index].Children.OfType<TextBlock>())
        {
            var subtitle = Equals(child.Tag, "subtitle");
            child.Foreground = on ? Palette.TextOnAccent : subtitle ? Palette.Muted : Palette.Text;
            child.Opacity = on && subtitle ? 0.85 : 1;
        }
        var n = _ops.Count;
        var angle = n <= 1 ? 0 : index * 360.0 / n * Math.PI / 180;
        var distance = on && n > 1 ? PopOut : 0;
        _moves[index].X = distance * Math.Sin(angle);
        _moves[index].Y = -distance * Math.Cos(angle);
    }

    private void UpdateHubText()
    {
        if (_repeat)
        {
            var last = _ops.Count > 0 ? _ops[0] : null;
            _title.Text = _highlight >= 0 && last is not null ? last.Label : T("Zopakovat", "Repeat");
            _detail.Text = last is null ? T("Poslední akce se sem nehodí", "Last action doesn't fit")
                : _highlight >= 0 ? T("poslední akce", "last action") : last.Label;
            return;
        }
        if (_highlight >= 0 && _highlight < _ops.Count)
        {
            var op = _ops[_highlight];
            _title.Text = op.Label;
            _detail.Text = op.Subtitle ?? Describe();
            return;
        }
        _title.Text = _items.Count == 0 ? "DropWheel" : WheelName(_wheel);
        _detail.Text = _items.Count == 0 ? T("Přetáhněte sem soubory", "Drop files here")
            : _ops.Count == 0 ? T("Pro tento výběr nic není", "Nothing for this selection")
            : Describe();
    }

    private string Describe() => _items.Count == 1 ? _items[0].Name : Items(_items.Count);

    /// <summary>Renders the wheel to a PNG (self-test), optionally with a slice highlighted.</summary>
    public void Highlight(int index) => SetHighlight(index);
}
