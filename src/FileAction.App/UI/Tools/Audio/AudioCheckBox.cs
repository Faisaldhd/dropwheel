using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>A themed checkbox: a rounded box with the accent gradient and a check glyph, plus its label.</summary>
internal sealed class AudioCheckBox : Border
{
    private readonly Border _box;
    private readonly TextBlock _check;
    private readonly TextBlock _label;
    private bool _checked;

    public AudioCheckBox(string text, bool isChecked)
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Padding = new Thickness(2, 4, 2, 4);

        _check = new TextBlock
        {
            Text = "\uE73E",
            FontFamily = Theme.Icons,
            FontSize = 11,
            Foreground = Theme.TextOnAccent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _box = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _check,
        };
        _label = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = Theme.Text,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_box);
        row.Children.Add(_label);
        Child = row;

        MouseLeftButtonUp += (_, e) =>
        {
            IsChecked = !IsChecked;
            e.Handled = true;
        };

        _checked = isChecked;
        Paint();
    }

    /// <summary>Raised when the user flips the box.</summary>
    public event Action<bool>? CheckedChanged;

    public bool IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Paint();
            CheckedChanged?.Invoke(value);
        }
    }

    /// <summary>Updates the visual state without raising <see cref="CheckedChanged"/>.</summary>
    public void SetChecked(bool value)
    {
        _checked = value;
        Paint();
    }

    /// <summary>Re-reads the shared palette after Windows switched light/dark.</summary>
    public void RefreshTheme()
    {
        _label.Foreground = Theme.Text;
        _check.Foreground = Theme.TextOnAccent;
        Paint();
    }

    private void Paint()
    {
        _box.Background = _checked ? Theme.AccentGradient : Brushes.Transparent;
        _box.BorderBrush = _checked ? Brushes.Transparent : Theme.Muted;
        _check.Visibility = _checked ? Visibility.Visible : Visibility.Collapsed;
    }
}
