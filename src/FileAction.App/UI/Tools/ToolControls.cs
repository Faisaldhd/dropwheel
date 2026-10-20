using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileAction.App.UI.Tools;

/// <summary>
/// Small controls the floating tools share: muted labels, the themed text box, a card wrapper and a pill
/// chooser whose selection can be restored (Reset has to put the chips back, not just the values).
/// </summary>
internal static class ToolControls
{
    /// <summary>A muted caption or hint.</summary>
    public static TextBlock Label(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A themed single-line box, or a wrapping multi-line one for chapter lists.</summary>
    public static TextBox Box(string text = "", double width = 88, bool multiline = false)
    {
        var box = new TextBox
        {
            Text = text,
            Width = multiline ? double.NaN : width,
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13,
            Background = Theme.Card,
            Foreground = Theme.Text,
            CaretBrush = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        if (multiline)
            box.Height = 84;
        return box;
    }

    /// <summary>A rounded card that holds one control group.</summary>
    public static Border Card(FrameworkElement child) => new()
    {
        Background = Theme.Card,
        BorderBrush = Theme.PanelBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(12, 10, 12, 10),
        Child = child,
    };

    /// <summary>A captioned card: muted title, the control, an optional hint line.</summary>
    public static Border Section(string title, FrameworkElement content, string? hint = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(title));
        content.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(content);
        if (!string.IsNullOrEmpty(hint))
        {
            var hintText = Label(hint, 11.5);
            hintText.Margin = new Thickness(0, 6, 0, 0);
            hintText.Opacity = 0.85;
            panel.Children.Add(hintText);
        }

        return Card(panel);
    }

    /// <summary>
    /// A checkbox row: a small square that fills with the accent colour and shows a tick when on, plus its
    /// label. Returns the row and a setter, so Reset can put the box back without firing the callback.
    /// </summary>
    public static (FrameworkElement Element, Action<bool> Set) CheckBox(string label, bool value, Action<bool>? changed = null)
    {
        var tick = new TextBlock
        {
            Text = "\uE73E",
            FontFamily = Theme.Icons,
            FontSize = 10,
            Foreground = Theme.TextOnAccent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var box = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = tick,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var text = Controls.Text(label, 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(8, 0, 0, 0);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        row.Children.Add(box);
        row.Children.Add(text);

        var on = value;
        void Paint(bool state)
        {
            box.Background = state ? Theme.AccentGradient : Brushes.Transparent;
            box.BorderBrush = state ? Brushes.Transparent : Theme.Muted;
            tick.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
        }

        void Set(bool state)
        {
            on = state;
            Paint(on);
        }

        Paint(on);
        row.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            Paint(on);
            changed?.Invoke(on);
        };
        return (row, Set);
    }

    /// <summary>
    /// A row of mutually exclusive pills on a track. Returns the element and the function that selects a
    /// value again, so Reset can repaint the chips without rebuilding the view.
    /// </summary>
    public static (Border Element, Action<T> Select) Chips<T>(
        IReadOnlyList<(string Label, T Value)> options,
        T selected,
        Action<T>? changed = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var pills = new List<(Border Pill, TextBlock Text, T Value)>();

        void Paint(T value)
        {
            foreach (var (pill, text, candidate) in pills)
            {
                var on = EqualityComparer<T>.Default.Equals(candidate, value);
                pill.Background = on ? Theme.AccentGradient : Brushes.Transparent;
                text.Foreground = on ? Theme.TextOnAccent : Theme.Text;
                text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        foreach (var (label, value) in options)
        {
            var text = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11, 5, 11, 6),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = text,
            };
            var candidate = value;
            pill.MouseLeftButtonUp += (_, _) =>
            {
                Paint(candidate);
                changed?.Invoke(candidate);
            };
            pills.Add((pill, text, value));
            panel.Children.Add(pill);
        }

        Paint(selected);
        var track = new Border
        {
            Background = Theme.Track,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(2),
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return (track, Paint);
    }
}
