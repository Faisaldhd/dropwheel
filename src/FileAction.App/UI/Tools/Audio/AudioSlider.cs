using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// A slim slider for a decimal value (loudness targets are not whole numbers): accent fill, round thumb
/// and the current value printed with Western digits. Technical values stay left-to-right in Arabic too.
/// </summary>
internal sealed class AudioSlider : Grid
{
    private const double ThumbSize = 16;

    private readonly double _min;
    private readonly double _max;
    private readonly double _step;
    private readonly string _format;
    private readonly Grid _surface;
    private readonly Border _track;
    private readonly Border _fill;
    private readonly Ellipse _thumb;
    private readonly TextBlock _valueText;
    private double _value;
    private bool _dragging;

    public AudioSlider(double min, double max, double value, double step, string format)
    {
        _min = min;
        _max = max;
        _step = Math.Max(step, 1e-6);
        _format = format;
        MinWidth = 120;
        MinHeight = 24;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _track = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.Track,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Theme.AccentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _thumb = new Ellipse
        {
            Width = ThumbSize,
            Height = ThumbSize,
            Fill = Theme.TextOnAccent,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Theme.Shadow(6),
        };
        _surface = new Grid
        {
            Height = MinHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            FlowDirection = FlowDirection.LeftToRight,
        };
        _surface.Children.Add(_track);
        _surface.Children.Add(_fill);
        _surface.Children.Add(_thumb);
        _surface.SizeChanged += (_, _) => Paint();
        _surface.MouseLeftButtonDown += OnDown;
        _surface.MouseMove += OnMove;
        _surface.MouseLeftButtonUp += OnUp;

        _valueText = new TextBlock
        {
            Width = 42,
            FontSize = 12.5,
            Foreground = Theme.Text,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
        };

        SetColumn(_surface, 0);
        SetColumn(_valueText, 1);
        Children.Add(_surface);
        Children.Add(_valueText);

        _value = Snap(value);
        Paint();
    }

    /// <summary>Raised while the thumb moves (the value label is already updated).</summary>
    public event Action<double>? ValueChanged;

    /// <summary>Raised when the drag ends, so the expensive analysis runs once.</summary>
    public event Action<double>? Committed;

    public double Value => _value;

    public void SetValue(double value)
    {
        var snapped = Snap(value);
        if (Math.Abs(snapped - _value) < 1e-9)
            return;
        _value = snapped;
        Paint();
    }

    /// <summary>Re-reads the shared palette after Windows switched light/dark.</summary>
    public void RefreshTheme()
    {
        _track.Background = Theme.Track;
        _fill.Background = Theme.AccentGradient;
        _thumb.Fill = Theme.TextOnAccent;
        _thumb.Stroke = Theme.PanelBorder;
        _valueText.Foreground = Theme.Text;
        InvalidateVisual();
    }

    private void SetFrom(double x)
    {
        var width = Math.Max(1, _surface.ActualWidth);
        var travel = Math.Max(1, width - ThumbSize);
        var t = Math.Clamp((x - ThumbSize / 2) / travel, 0, 1);
        var next = Snap(_min + t * (_max - _min));
        if (Math.Abs(next - _value) < 1e-9)
            return;
        _value = next;
        Paint();
        ValueChanged?.Invoke(_value);
    }

    private double Snap(double value)
    {
        var clamped = Math.Clamp(value, _min, _max);
        var steps = Math.Round((clamped - _min) / _step);
        return Math.Clamp(_min + steps * _step, _min, _max);
    }

    private void Paint()
    {
        var width = Math.Max(1, _surface.ActualWidth);
        var travel = Math.Max(1, width - ThumbSize);
        var t = (_value - _min) / Math.Max(1e-9, _max - _min);
        _fill.Width = Math.Max(0, ThumbSize / 2 + t * travel);
        _thumb.Margin = new Thickness(t * travel, 0, 0, 0);
        _valueText.Text = _value.ToString(_format, CultureInfo.InvariantCulture);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _surface.CaptureMouse();
        SetFrom(e.GetPosition(_surface).X);
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            SetFrom(e.GetPosition(_surface).X);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        _surface.ReleaseMouseCapture();
        Committed?.Invoke(_value);
        e.Handled = true;
    }
}
