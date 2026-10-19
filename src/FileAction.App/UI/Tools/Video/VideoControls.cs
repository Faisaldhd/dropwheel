using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Video;

/// <summary>Small video-tool widgets built in code and coloured from <see cref="Theme"/>.</summary>
internal static class VideoControls
{
    /// <summary>A rounded chip; call <see cref="PaintChip"/> to show the selected state.</summary>
    public static Border Chip(string text, Action click)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Theme.Track,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontSize = 12, Foreground = Theme.Text },
        };
        chip.MouseLeftButtonUp += (_, _) => click();
        return chip;
    }

    public static void PaintChip(Border chip, bool selected)
    {
        chip.Background = selected ? Theme.Accent : Theme.Track;
        if (chip.Child is TextBlock label)
            label.Foreground = selected ? Theme.TextOnAccent : Theme.Text;
    }

    /// <summary>A round icon button (one frame back/forward).</summary>
    public static Border IconButton(string glyph, string tooltip, Action click)
    {
        var button = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Theme.Track,
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = UiSetup.Tip(tooltip),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = 10,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.MouseEnter += (_, _) => button.Opacity = 0.85;
        button.MouseLeave += (_, _) => button.Opacity = 1;
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    /// <summary>The small muted hint line under a control.</summary>
    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };
}

/// <summary>
/// An "hh:mm:ss.mmm" box: parses on Enter or focus loss, reverts on Escape, and never overwrites what the
/// user is typing. Numeric text stays left-to-right in Arabic too.
/// </summary>
internal sealed class VideoTimeBox : TextBox
{
    private TimeSpan _value;

    public VideoTimeBox()
    {
        Width = 98;
        Height = 30;
        Padding = new Thickness(6, 4, 6, 4);
        Background = Theme.Card;
        Foreground = Theme.Text;
        BorderBrush = Theme.PanelBorder;
        BorderThickness = new Thickness(1);
        FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI");
        FontSize = 12;
        TextAlignment = TextAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        FlowDirection = FlowDirection.LeftToRight;
        CaretBrush = Theme.Text;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ShowValue();
                e.Handled = true;
            }
        };
        LostFocus += (_, _) => Commit();
        ShowValue();
    }

    /// <summary>Raised after a successful parse; the view clamps the value and writes it back.</summary>
    public event Action<TimeSpan>? Committed;

    public TimeSpan Value
    {
        get => _value;
        set
        {
            _value = value;
            if (!IsKeyboardFocusWithin)
                ShowValue();
        }
    }

    public void ShowValue() => Text = VideoEdit.FormatTime(_value);

    private void Commit()
    {
        if (VideoEdit.TryParseTime(Text, out var parsed))
            _value = parsed;
        ShowValue();
        Committed?.Invoke(_value);
    }
}

/// <summary>
/// The 0.25×–4× speed slider. Positions are logarithmic so 1× sits in the middle of the track; values
/// snap to 0.05 so a preset always lights up its chip.
/// </summary>
internal sealed class VideoRateSlider : Grid
{
    private const double SurfaceWidth = 300;
    private const double ThumbSize = 18;
    private const double Step = 0.05;

    private readonly Border _fill;
    private readonly Ellipse _thumb;
    private readonly TextBlock _label;
    private readonly Grid _surface;
    private double _value = 1;

    public VideoRateSlider()
    {
        Minimum = VideoEdit.MinSpeed;
        Maximum = VideoEdit.MaxSpeed;
        FlowDirection = FlowDirection.LeftToRight;

        var track = new Border
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
            Fill = Brushes.White,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = Theme.Shadow(6),
        };
        _surface = new Grid
        {
            Width = SurfaceWidth,
            Height = 26,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        _surface.Children.Add(track);
        _surface.Children.Add(_fill);
        _surface.Children.Add(_thumb);
        _surface.MouseLeftButtonDown += (_, e) =>
        {
            _surface.CaptureMouse();
            SetFrom(e.GetPosition(_surface));
        };
        _surface.MouseMove += (_, e) =>
        {
            if (_surface.IsMouseCaptured)
                SetFrom(e.GetPosition(_surface));
        };
        _surface.MouseLeftButtonUp += (_, _) => _surface.ReleaseMouseCapture();

        _label = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            MinWidth = 54,
        };

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Children.Add(_surface);
        Children.Add(_label);
        Grid.SetColumn(_surface, 0);
        Grid.SetColumn(_label, 1);

        SetValueCore(1, raise: false);
    }

    public event Action<double>? ValueChanged;

    public double Minimum { get; }

    public double Maximum { get; }

    public double Value
    {
        get => _value;
        set => SetValueCore(value, raise: false);
    }

    /// <summary>Moves the slider without raising <see cref="ValueChanged"/> (preset buttons do this).</summary>
    public void SetValue(double value) => SetValueCore(value, raise: false);

    private void SetFrom(Point point)
    {
        var t = Math.Clamp((point.X - ThumbSize / 2) / (SurfaceWidth - ThumbSize), 0, 1);
        var raw = Minimum * Math.Pow(Maximum / Minimum, t);
        var snapped = Math.Round(raw / Step) * Step;
        SetValueCore(snapped, raise: true);
    }

    private void SetValueCore(double value, bool raise)
    {
        if (!double.IsFinite(value))
            value = 1;
        _value = Math.Clamp(value, Minimum, Maximum);
        var t = Math.Log(_value / Minimum) / Math.Log(Maximum / Minimum);
        _fill.Width = Math.Max(0, t * (SurfaceWidth - ThumbSize) + ThumbSize / 2);
        _thumb.Margin = new Thickness(t * (SurfaceWidth - ThumbSize), 0, 0, 0);
        _label.Text = VideoEdit.SpeedText(_value);
        if (raise)
            ValueChanged?.Invoke(_value);
    }
}
