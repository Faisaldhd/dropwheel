using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FileAction.App.UI;

/// <summary>
/// Small Windows 11–style building blocks drawn in code (cards, segmented choices, toggles,
/// sliders, pill buttons), so they look the same everywhere without depending on a theme assembly.
/// </summary>
internal static class Controls
{
    public static TextBlock Text(string text, double size = Design.Type.Body, FontWeight? weight = null, Brush? brush = null, bool wrap = true) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush ?? Theme.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontFamily = Theme.DisplayFont,
        FontSize = Design.Type.Heading,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Text,
        Margin = new Thickness(Design.Space.Xs, Design.Space.Xxxl, 0, Design.Space.Md),
    };

    /// <summary>A rounded card with a title/description on the left and a control on the right.</summary>
    public static Border Card(string title, string? description, UIElement? control, UIElement? below = null, string? glyph = null)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Text(title, Design.Type.Body, FontWeights.Normal));
        if (!string.IsNullOrEmpty(description))
        {
            var d = Text(description, Design.Type.Caption, brush: Theme.Muted);
            d.Margin = new Thickness(0, Design.Space.Xs, 0, 0);
            left.Children.Add(d);
        }

        var row = new DockPanel { LastChildFill = true };
        if (glyph is not null)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = Design.Icon.Header,
                Foreground = Theme.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, Design.Space.Xl, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
        }
        if (control is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(16, 0, 0, 0);
            DockPanel.SetDock(fe, Dock.Right);
            row.Children.Add(fe);
        }
        row.Children.Add(left);

        var content = new StackPanel();
        content.Children.Add(row);
        if (below is not null)
            content.Children.Add(below);

        return new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
        };
    }

    /// <summary>A row of mutually exclusive pills; the active one is filled with the accent gradient.</summary>
    public static Border Segmented<T>(IReadOnlyList<(string Label, T Value)> options, T selected, Action<T> changed)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var pills = new List<(Border Pill, TextBlock Text, T Value)>();
        void Select(T value)
        {
            foreach (var (pill, text, v) in pills)
            {
                var on = EqualityComparer<T>.Default.Equals(v, value);
                pill.Background = on ? Theme.AccentGradient : Brushes.Transparent;
                text.Foreground = on ? Theme.TextOnAccent : Theme.Text;
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        foreach (var (label, value) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(12, 5, 12, 6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = text,
            };
            var v = value;
            pill.MouseLeftButtonUp += (_, _) =>
            {
                Select(v);
                changed(v);
            };
            Motion.Interactive(pill);
            pills.Add((pill, text, value));
            panel.Children.Add(pill);
        }
        Select(selected);
        return new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            Child = panel,
        };
    }

    /// <summary>Windows 11 toggle switch.</summary>
    public static FrameworkElement Toggle(bool value, Action<bool> changed)
    {
        var knob = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border
        {
            Width = 40,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = knob,
        };
        var translate = new TranslateTransform();
        knob.RenderTransform = translate;
        var on = value;
        void Paint(bool animate)
        {
            track.Background = on ? Theme.AccentGradient : Brushes.Transparent;
            track.BorderBrush = on ? Brushes.Transparent : Theme.Muted;
            knob.Fill = on ? Brushes.White : Theme.Muted;
            var x = on ? 23.0 : 3.0;
            if (animate && Motion.Enabled)
                translate.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(x, TimeSpan.FromMilliseconds(Motion.ToggleMs)) { EasingFunction = Motion.SoftOut });
            else
                translate.X = x;
        }
        track.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            Paint(true);
            changed(on);
        };
        Motion.Interactive(track);
        Paint(false);
        return track;
    }

    /// <summary>A slim slider with an accent-filled track and a round thumb.</summary>
    public static FrameworkElement Slider(int min, int max, int value, Action<int> changed, Action committed)
    {
        const double width = 200;
        var label = new TextBlock { Width = 40, FontSize = 13, Foreground = Theme.Text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.AccentGradient, HorizontalAlignment = HorizontalAlignment.Left };
        var thumb = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = Brushes.White,
            Stroke = Theme.PanelBorder,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Effect = Theme.Shadow(6),
        };
        var surface = new Grid { Width = width, Height = 24, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        surface.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Theme.Track });
        surface.Children.Add(fill);
        surface.Children.Add(thumb);

        var current = value;
        void Show()
        {
            var t = (current - min) / (double)(max - min);
            fill.Width = Math.Max(0, t * width);
            thumb.Margin = new Thickness(t * (width - 18), 0, 0, 0);
            // Technical values keep Western digits in Arabic too.
            label.Text = current.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        void SetFrom(Point p)
        {
            var t = Math.Clamp((p.X - 9) / (width - 18), 0, 1);
            var v = (int)Math.Round(min + t * (max - min));
            if (v == current)
                return;
            current = v;
            Show();
            changed(v);
        }
        surface.MouseLeftButtonDown += (_, e) =>
        {
            surface.CaptureMouse();
            SetFrom(e.GetPosition(surface));
        };
        surface.MouseMove += (_, e) =>
        {
            if (surface.IsMouseCaptured)
                SetFrom(e.GetPosition(surface));
        };
        surface.MouseLeftButtonUp += (_, _) =>
        {
            surface.ReleaseMouseCapture();
            committed();
        };
        Show();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Motion.Interactive(thumb);
        row.Children.Add(surface);
        row.Children.Add(label);
        return row;
    }

    /// <summary>A rounded button; <paramref name="primary"/> ones use the accent gradient.</summary>
    public static Border Button(string text, Action click, bool primary = false, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var foreground = primary ? Theme.TextOnAccent : Theme.Text;
        if (glyph is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = 12,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 7, 0),
            });
        }
        content.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = foreground, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        var normal = primary ? Theme.AccentGradient : Theme.Card;
        var button = new Border
        {
            Background = normal,
            BorderBrush = primary ? Brushes.Transparent : Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            Cursor = Cursors.Hand,
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Motion.Interactive(button);
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }

    /// <summary>Replaces the caption of a button built by <see cref="Button"/> without losing its glyph.</summary>
    public static void SetText(Border button, string text)
    {
        if (button.Child is StackPanel panel && panel.Children.Count > 0
            && panel.Children[panel.Children.Count - 1] is TextBlock label)
            label.Text = text;
    }

    /// <summary>A small coloured status label, e.g. "found" / "missing".</summary>
    public static Border Badge(string text, bool good) => new()
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 2, 8, 3),
        Background = new SolidColorBrush(Color.FromArgb(0x26, good ? (byte)0x3C : (byte)0xF0, good ? (byte)0xC3 : (byte)0x5A, good ? (byte)0x6E : (byte)0x5A)),
        Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = good ? Theme.BadgeSuccessText : Theme.BadgeErrorText },
    };
}
