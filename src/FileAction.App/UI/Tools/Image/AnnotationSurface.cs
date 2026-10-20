using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// The drawing surface inside the Annotate window: it paints the downsampled photo plus the flattened
/// annotation primitives, and turns mouse gestures into document edits. Everything it stores is in
/// original image pixels; only the drawing and hit-testing convert to window coordinates. The view owns
/// the toolbar and the property controls; this element owns the objects and the undo history.
/// </summary>
internal sealed class AnnotationSurface : FrameworkElement
{
    /// <summary>Leaves room around the photo for selection handles that sit on its edge.</summary>
    private const double FramePadding = 10;

    private const double HandleRadius = 4.5;

    /// <summary>How close (in window DIP) the pointer must be to a handle or an object.</summary>
    private const double HandleTolerance = 10;

    private const double HitTolerance = 8;

    private static readonly SolidColorBrush HandleFill = Frozen(Colors.White);

    private static readonly SolidColorBrush Surface = Frozen(Colors.Transparent);

    private readonly AnnotationHistory _history = new();
    private readonly List<BitmapSource> _stamps = [];

    private BitmapSource? _image;
    private AnnotationDocument? _document;
    private int _imageWidth;
    private int _imageHeight;
    private AnnotationTool _tool = AnnotationTool.Select;
    private AnnotationColor _color = AnnotationColor.Red;
    private double _strokeWidth = 4;
    private double _fontSize = 20;
    private bool _filled;
    private int _selected = -1;
    private int _activeStamp = -1;

    private GestureMode _mode;
    private AnnotationObject? _dragObject;
    private AnnotationPoint _dragStart;
    private AnnotationHandle _handle = AnnotationHandle.None;
    private AnnotationObject? _pending;
    private AnnotationTool _pendingTool = AnnotationTool.Select;
    private List<AnnotationPoint>? _trace;
    private bool _checkpointed;

    public AnnotationSurface()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Arrow;
    }

    private enum GestureMode
    {
        None,
        Create,
        Move,
        Resize,
    }

    /// <summary>Raised when the object list or the selection changed, so the buttons can refresh.</summary>
    public event Action? Changed;

    /// <summary>Raised when a different object (or none) is selected.</summary>
    public event Action? SelectionChanged;

    /// <summary>A Text-tool click: the view should open its editor for a new text object at this point.</summary>
    public event Action<AnnotationPoint>? TextRequested;

    /// <summary>A double-click on an existing text object: the view should edit it.</summary>
    public event Action<int>? TextEditRequested;

    public AnnotationDocument? Document => _document;

    public IReadOnlyList<BitmapSource> Stamps => _stamps;

    public int SelectedIndex => _selected;

    public AnnotationObject? Selected =>
        _document is not null && _selected >= 0 && _selected < _document.Count
            ? _document.Objects[_selected]
            : null;

    public AnnotationTool Tool => _tool;

    public AnnotationColor Color => _color;

    public double StrokeWidth => _strokeWidth;

    public double FontSize => _fontSize;

    public bool Filled => _filled;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public bool HasSelection => Selected is not null;

    public bool HasObjects => _document is { Count: > 0 };

    /// <summary>True once at least one stamp image has been loaded with <see cref="AddStamp"/>.</summary>
    public bool HasStamps => _stamps.Count > 0;

    /// <summary>Starts a fresh document for a newly loaded image and clears all editing state.</summary>
    public void SetImage(BitmapSource image, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        _image = image;
        _imageWidth = Math.Max(1, width);
        _imageHeight = Math.Max(1, height);
        _document = new AnnotationDocument(_imageWidth, _imageHeight);
        _history.Clear();
        _selected = -1;
        CancelPending();
        InvalidateVisual();
    }

    public void SetTool(AnnotationTool tool)
    {
        if (_tool == tool)
            return;
        _tool = tool;
        CancelPending();
        Cursor = tool == AnnotationTool.Select ? Cursors.Arrow : Cursors.Cross;
        InvalidateVisual();
    }

    public void SetColor(AnnotationColor color)
    {
        _color = color;
        ApplyToSelection(obj => obj with { Color = color });
    }

    public void SetStrokeWidth(double width)
    {
        _strokeWidth = Math.Clamp(width, 1, 64);
        ApplyToSelection(obj => obj with { StrokeWidth = _strokeWidth });
    }

    public void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, 6, 200);
        ApplyToSelection(obj => obj with { FontSize = _fontSize });
    }

    public void SetFilled(bool filled)
    {
        _filled = filled;
        ApplyToSelection(obj => obj with { Filled = filled });
    }

    /// <summary>Adds an image the user picked, selects the stamp tool and returns the stamp index.</summary>
    public int AddStamp(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _stamps.Add(bitmap);
        _activeStamp = _stamps.Count - 1;
        SetTool(AnnotationTool.Stamp);
        Changed?.Invoke();
        return _activeStamp;
    }

    /// <summary>Creates a text object whose box is measured from the text itself.</summary>
    public void CreateText(AnnotationPoint at, string text, bool rtl)
    {
        if (_document is null || string.IsNullOrWhiteSpace(text))
            return;
        var maxWidth = Math.Max(24, _imageWidth * 0.9);
        var size = MeasureText(text, _fontSize, rtl, maxWidth);
        var x = Math.Clamp(at.X, 0, Math.Max(0, _imageWidth - size.Width));
        var y = Math.Clamp(at.Y, 0, Math.Max(0, _imageHeight - size.Height));
        var obj = new AnnotationObject(
            AnnotationKind.Text, new AnnotationRect(x, y, size.Width, size.Height), _color,
            _strokeWidth, _fontSize, _filled, text, rtl);
        _history.Push(_document);
        _selected = _document.Add(obj);
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Replaces the text of an existing text object; an empty text removes the object.</summary>
    public void ReplaceText(int index, string text, bool rtl)
    {
        if (_document is null || index < 0 || index >= _document.Count)
            return;
        var existing = _document.Objects[index];
        if (existing.Kind != AnnotationKind.Text)
            return;

        _history.Push(_document);
        if (string.IsNullOrWhiteSpace(text))
        {
            _document.RemoveAt(index);
            NormalizeSelection();
        }
        else
        {
            var maxWidth = Math.Max(24, _imageWidth * 0.9);
            var size = MeasureText(text, existing.FontSize, rtl, maxWidth);
            var bounds = new AnnotationRect(existing.Bounds.X, existing.Bounds.Y, size.Width, size.Height)
                .ClampInside(_imageWidth, _imageHeight);
            _document.Replace(index, existing with { Text = text, Rtl = rtl, Bounds = bounds });
            _selected = index;
        }
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void Undo()
    {
        if (_document is null || !_history.Undo(_document))
            return;
        NormalizeSelection();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void Redo()
    {
        if (_document is null || !_history.Redo(_document))
            return;
        NormalizeSelection();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void DeleteSelected()
    {
        if (_document is null || Selected is null)
            return;
        _history.Push(_document);
        _document.RemoveAt(_selected);
        NormalizeSelection();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void BringToFront()
    {
        if (_document is null || Selected is null || _selected >= _document.Count - 1)
            return;
        _history.Push(_document);
        _document.BringToFront(_selected);
        _selected = _document.Count - 1;
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SendToBack()
    {
        if (_document is null || Selected is null || _selected <= 0)
            return;
        _history.Push(_document);
        _document.SendToBack(_selected);
        _selected = 0;
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Back to the state the window opened in: no objects, no selection, default style.</summary>
    public void Reset()
    {
        CancelPending();
        if (_imageWidth > 0 && _imageHeight > 0)
            _document = new AnnotationDocument(_imageWidth, _imageHeight);
        _history.Clear();
        _selected = -1;
        _tool = AnnotationTool.Select;
        _color = AnnotationColor.Red;
        _strokeWidth = 4;
        _fontSize = 20;
        _filled = false;
        Cursor = Cursors.Arrow;
        Changed?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(
            Surface, null, new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)));

        if (_image is null || !TryGetTransform(out var scale, out var originX, out var originY))
            return;

        var photo = new Rect(originX, originY, _imageWidth * scale, _imageHeight * scale);
        // A neutral plate behind the photo keeps transparent PNGs readable on the card.
        drawingContext.DrawRectangle(Theme.Track, null, photo);
        drawingContext.DrawImage(_image, photo);

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_document is not null)
        {
            AnnotationRenderer.Draw(
                drawingContext, _document.Flatten(), _image, _imageWidth, _imageHeight,
                originX, originY, scale, _stamps, pixelsPerDip);
        }
        if (_pending is not null)
        {
            AnnotationRenderer.Draw(
                drawingContext, AnnotationMath.Flatten(_pending, _imageWidth, _imageHeight), _image,
                _imageWidth, _imageHeight, originX, originY, scale, _stamps, pixelsPerDip);
        }

        DrawSelection(drawingContext, scale, originX, originY);
    }

    private void DrawSelection(DrawingContext dc, double scale, double originX, double originY)
    {
        var selected = Selected;
        if (selected is null || _mode == GestureMode.Create)
            return;

        var outline = new Pen(Theme.Accent, 1.4) { DashStyle = new DashStyle([4, 3], 0) };
        if (selected.Kind is AnnotationKind.Arrow or AnnotationKind.Line)
        {
            var (from, to) = AnnotationMath.Segment(selected);
            dc.DrawLine(outline, MapPoint(from, scale, originX, originY), MapPoint(to, scale, originX, originY));
            DrawHandle(dc, MapPoint(from, scale, originX, originY));
            DrawHandle(dc, MapPoint(to, scale, originX, originY));
            return;
        }

        dc.DrawRectangle(null, outline, MapRect(selected.Bounds, scale, originX, originY));
        foreach (var handle in AnnotationMath.BoxHandles)
        {
            var point = AnnotationMath.HandlePoint(selected, handle);
            DrawHandle(dc, MapPoint(point, scale, originX, originY));
        }
    }

    private static void DrawHandle(DrawingContext dc, Point center) =>
        dc.DrawEllipse(HandleFill, new Pen(Theme.Accent, 1.4), center, HandleRadius, HandleRadius);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_image is null || _document is null)
            return;
        if (!TryToImage(e.GetPosition(this), out var point))
            return;

        if (_tool == AnnotationTool.Select)
        {
            SelectAt(point, e.ClickCount);
            return;
        }

        if (_tool == AnnotationTool.Text)
        {
            TextRequested?.Invoke(point);
            e.Handled = true;
            return;
        }

        BeginCreate(point);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mode == GestureMode.None)
        {
            UpdateCursor(e.GetPosition(this));
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndGesture();
            return;
        }
        if (!TryToImage(e.GetPosition(this), out var point))
            return;

        if (_mode == GestureMode.Create)
        {
            if (_trace is not null)
            {
                var minimumStep = Math.Max(1, 2 / Math.Max(0.0001, Scale));
                if (point.DistanceTo(_trace[^1]) >= minimumStep)
                    _trace.Add(point);
                _pending = BuildObject(_pendingTool, _dragStart, point, _trace);
            }
            else
            {
                _pending = BuildObject(_pendingTool, _dragStart, point, null);
            }
            InvalidateVisual();
            return;
        }

        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        if (dx == 0 && dy == 0)
            return;

        var updated = _mode == GestureMode.Move
            ? AnnotationMath.Move(_dragObject!, dx, dy, _imageWidth, _imageHeight)
            : AnnotationMath.Resize(_dragObject!, _handle, dx, dy, _imageWidth, _imageHeight);

        if (!_checkpointed)
        {
            // The snapshot is taken before the first real movement, so undo restores the object as it
            // was when the gesture started, not one mouse-move later.
            _history.Push(_document!);
            _checkpointed = true;
        }
        _document!.Replace(_selected, updated);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mode == GestureMode.Create)
            CommitCreate();
        if (_mode != GestureMode.None)
            EndGesture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_mode != GestureMode.None)
        {
            CancelPending();
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            _mode = GestureMode.None;
            _dragObject = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_selected != -1)
        {
            _selected = -1;
            SelectionChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _mode = GestureMode.None;
        _dragObject = null;
        _pending = null;
        _trace = null;
        _checkpointed = false;
        InvalidateVisual();
    }

    private void SelectAt(AnnotationPoint point, int clickCount)
    {
        var selected = Selected;
        if (selected is not null)
        {
            var handle = AnnotationMath.HandleAt(selected, point, HandleTolerance / Scale);
            if (handle != AnnotationHandle.None)
            {
                BeginGesture(GestureMode.Resize, selected, point, handle);
                return;
            }
        }

        var hit = _document is null ? -1 : AnnotationMath.HitTest(_document.Objects, point, HitTolerance / Scale);
        if (hit < 0)
        {
            if (_selected != -1)
            {
                _selected = -1;
                SelectionChanged?.Invoke();
                InvalidateVisual();
            }
            return;
        }

        var obj = _document!.Objects[hit];
        if (clickCount >= 2 && obj.Kind == AnnotationKind.Text)
        {
            TextEditRequested?.Invoke(hit);
            return;
        }

        if (_selected != hit)
        {
            _selected = hit;
            SelectionChanged?.Invoke();
        }
        InvalidateVisual();
        BeginGesture(GestureMode.Move, obj, point, AnnotationHandle.Move);
    }

    private void BeginGesture(GestureMode mode, AnnotationObject obj, AnnotationPoint point, AnnotationHandle handle)
    {
        _mode = mode;
        _dragObject = obj;
        _dragStart = point;
        _handle = handle;
        _checkpointed = false;
        CaptureMouse();
    }

    private void BeginCreate(AnnotationPoint point)
    {
        _mode = GestureMode.Create;
        _pendingTool = _tool;
        _dragStart = point;
        _pending = null;
        _trace = _pendingTool is AnnotationTool.Pen or AnnotationTool.Highlighter ? [point] : null;
        CaptureMouse();
        InvalidateVisual();
    }

    private void CommitCreate()
    {
        // A click (no drag) still creates the tools that have a sensible default size: a numbered
        // callout, a magnifier lens or a stamp image. Shapes and strokes need a real drag.
        var obj = _pending ?? BuildObject(_pendingTool, _dragStart, _dragStart, _trace);
        _pending = null;
        if (obj is null || _document is null || !IsCommittable(obj))
            return;
        _history.Push(_document);
        _selected = _document.Add(obj);
        Changed?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void EndGesture()
    {
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _mode = GestureMode.None;
        _dragObject = null;
        _handle = AnnotationHandle.None;
        _trace = null;
        _pending = null;
        _checkpointed = false;
        InvalidateVisual();
    }

    private void CancelPending()
    {
        _pending = null;
        _trace = null;
        _checkpointed = false;
    }

    /// <summary>Builds the object a drag would create, or null while the drag is still too small.</summary>
    private AnnotationObject? BuildObject(
        AnnotationTool tool, AnnotationPoint from, AnnotationPoint to, List<AnnotationPoint>? trace)
    {
        var kind = AnnotationMath.KindFor(tool);
        if (kind is null)
            return null;

        var drag = AnnotationRect.FromPoints(from.X, from.Y, to.X, to.Y);
        switch (kind.Value)
        {
            case AnnotationKind.Arrow:
            case AnnotationKind.Line:
                if (from.DistanceTo(to) < AnnotationMath.MinDragSize)
                    return null;
                return AnnotationObject.Segment(kind.Value, from, to, _color, _strokeWidth, _fontSize);

            case AnnotationKind.Pen:
            case AnnotationKind.Highlighter:
            {
                var points = trace is { Count: >= 2 } ? trace.ToArray() : [];
                if (points.Length < 2)
                    return null;
                return AnnotationObject.Polyline(kind.Value, points, _color, _strokeWidth, _fontSize);
            }

            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
            {
                if (drag.Width < AnnotationMath.MinDragSize || drag.Height < AnnotationMath.MinDragSize)
                    return null;
                var bounds = AnnotationMath.ClampInside(drag, _imageWidth, _imageHeight);
                return new AnnotationObject(kind.Value, bounds, _color, _strokeWidth, _fontSize, _filled);
            }

            case AnnotationKind.Callout:
            {
                var side = Math.Max(drag.Width, drag.Height);
                if (side < 24)
                    side = Math.Clamp(Math.Min(_imageWidth, _imageHeight) * 0.06, 28, 120);
                var number = _document?.NextCalloutNumber() ?? 1;
                return new AnnotationObject(
                    AnnotationKind.Callout, SquareAt(drag.Center, side), _color, _strokeWidth, _fontSize,
                    Filled: true, Number: number);
            }

            case AnnotationKind.Magnifier:
            {
                var side = Math.Max(drag.Width, drag.Height);
                if (side < 40)
                    side = Math.Clamp(Math.Min(_imageWidth, _imageHeight) * 0.25, 60, 400);
                return new AnnotationObject(
                    AnnotationKind.Magnifier, SquareAt(drag.Center, side), _color,
                    Math.Max(3, _strokeWidth), _fontSize);
            }

            case AnnotationKind.Stamp:
            {
                if (_activeStamp < 0 || _activeStamp >= _stamps.Count)
                    return null;
                return new AnnotationObject(
                    AnnotationKind.Stamp, StampBounds(drag, _stamps[_activeStamp]), _color, _strokeWidth,
                    _fontSize, StampIndex: _activeStamp);
            }

            default:
                return null;
        }
    }

    private static bool IsCommittable(AnnotationObject obj) => obj.Kind switch
    {
        AnnotationKind.Arrow or AnnotationKind.Line or AnnotationKind.Pen or AnnotationKind.Highlighter =>
            obj.Path.Count >= 2,
        _ => obj.Bounds.Width >= AnnotationMath.MinSize && obj.Bounds.Height >= AnnotationMath.MinSize,
    };

    private AnnotationRect SquareAt(AnnotationPoint center, double side)
    {
        var limit = Math.Max(AnnotationMath.MinSize, Math.Min(_imageWidth, _imageHeight));
        side = Math.Clamp(side, AnnotationMath.MinSize, limit);
        var x = Math.Clamp(center.X - side / 2, 0, Math.Max(0, _imageWidth - side));
        var y = Math.Clamp(center.Y - side / 2, 0, Math.Max(0, _imageHeight - side));
        return new AnnotationRect(x, y, side, side);
    }

    private AnnotationRect StampBounds(AnnotationRect drag, BitmapSource bitmap)
    {
        var aspect = bitmap.PixelWidth / (double)Math.Max(1, bitmap.PixelHeight);
        double width, height;
        if (drag.Width >= AnnotationMath.MinDragSize && drag.Height >= AnnotationMath.MinDragSize)
        {
            width = Math.Min(drag.Width, drag.Height * aspect);
            height = width / Math.Max(0.0001, aspect);
        }
        else
        {
            width = Math.Max(48, Math.Min(_imageWidth * 0.3, bitmap.PixelWidth));
            height = width / Math.Max(0.0001, aspect);
        }

        var fit = Math.Min(1.0, Math.Min(_imageWidth / Math.Max(1, width), _imageHeight / Math.Max(1, height)));
        width = Math.Max(AnnotationMath.MinSize, width * fit);
        height = Math.Max(AnnotationMath.MinSize, height * fit);

        var x = Math.Clamp(drag.X + (drag.Width - width) / 2, 0, Math.Max(0, _imageWidth - width));
        var y = Math.Clamp(drag.Y + (drag.Height - height) / 2, 0, Math.Max(0, _imageHeight - height));
        return new AnnotationRect(x, y, width, height);
    }

    private void ApplyToSelection(Func<AnnotationObject, AnnotationObject> change)
    {
        var selected = Selected;
        if (selected is null || _document is null)
            return;
        _history.Push(_document);
        _document.Replace(_selected, change(selected));
        Changed?.Invoke();
        InvalidateVisual();
    }

    private void NormalizeSelection()
    {
        if (_document is null || _document.Count == 0)
        {
            _selected = -1;
            return;
        }
        if (_selected >= _document.Count)
            _selected = _document.Count - 1;
    }

    private void UpdateCursor(Point view)
    {
        if (_tool != AnnotationTool.Select || !TryGetTransform(out var scale, out _, out _))
        {
            Cursor = Cursors.Cross;
            return;
        }
        if (!TryToImage(view, out var point))
        {
            Cursor = Cursors.Arrow;
            return;
        }

        var selected = Selected;
        if (selected is not null)
        {
            var handle = AnnotationMath.HandleAt(selected, point, HandleTolerance / scale);
            if (handle != AnnotationHandle.None)
            {
                Cursor = CursorFor(handle);
                return;
            }
        }

        Cursor = _document is not null && AnnotationMath.HitTest(_document.Objects, point, HitTolerance / scale) >= 0
            ? Cursors.SizeAll
            : Cursors.Arrow;
    }

    private static Cursor CursorFor(AnnotationHandle handle) => handle switch
    {
        AnnotationHandle.TopLeft or AnnotationHandle.BottomRight => Cursors.SizeNWSE,
        AnnotationHandle.TopRight or AnnotationHandle.BottomLeft => Cursors.SizeNESW,
        AnnotationHandle.Top or AnnotationHandle.Bottom => Cursors.SizeNS,
        AnnotationHandle.Left or AnnotationHandle.Right => Cursors.SizeWE,
        AnnotationHandle.Start or AnnotationHandle.End => Cursors.Cross,
        _ => Cursors.SizeAll,
    };

    // ------------------------------------------------------------------ mapping

    private double Scale => TryGetTransform(out var scale, out _, out _) ? scale : 1;

    /// <summary>Window DIP → original image pixels; the point is clamped to the image.</summary>
    private bool TryToImage(Point view, out AnnotationPoint point)
    {
        point = default;
        if (!TryGetTransform(out var scale, out var originX, out var originY))
            return false;
        point = new AnnotationPoint(
            Math.Clamp((view.X - originX) / scale, 0, _imageWidth),
            Math.Clamp((view.Y - originY) / scale, 0, _imageHeight));
        return true;
    }

    /// <summary>The image's scale and top-left corner inside the element, leaving room for the handles.</summary>
    private bool TryGetTransform(out double scale, out double originX, out double originY)
    {
        scale = 1;
        originX = 0;
        originY = 0;
        if (_image is null || _imageWidth <= 0 || _imageHeight <= 0)
            return false;

        var availableWidth = Math.Max(1, ActualWidth - FramePadding * 2);
        var availableHeight = Math.Max(1, ActualHeight - FramePadding * 2);
        if (ActualWidth <= 1 || ActualHeight <= 1)
            return false;

        scale = Math.Min(availableWidth / _imageWidth, availableHeight / _imageHeight);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            scale = 1;
        originX = (ActualWidth - _imageWidth * scale) / 2;
        originY = (ActualHeight - _imageHeight * scale) / 2;
        return true;
    }

    private static Point MapPoint(AnnotationPoint point, double scale, double originX, double originY) =>
        new(originX + point.X * scale, originY + point.Y * scale);

    private static Rect MapRect(AnnotationRect rect, double scale, double originX, double originY) =>
        new(originX + rect.X * scale, originY + rect.Y * scale, rect.Width * scale, rect.Height * scale);

    /// <summary>Measures a text object's box in image pixels with the same font the renderer uses.</summary>
    private static Size MeasureText(string text, double fontSize, bool rtl, double maxWidth)
    {
        var typeface = new Typeface(Theme.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            typeface,
            Math.Max(1, fontSize),
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            TextAlignment = rtl ? TextAlignment.Right : TextAlignment.Left,
        };
        return new Size(
            Math.Max(AnnotationMath.MinSize, Math.Min(formatted.Width, maxWidth)),
            Math.Max(fontSize, formatted.Height));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
