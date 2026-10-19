using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Password;

/// <summary>
/// A masked password box with an eye toggle. The visible text box and the PasswordBox stay in sync, so the
/// user can check a typo without either control ever being bound to a stored value.
/// </summary>
internal sealed class PasswordField : Grid
{
    private readonly PasswordBox _masked = new()
    {
        PasswordChar = '\u25CF',
        MaxLength = PasswordRules.MaximumLength,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Theme.Text,
        CaretBrush = Theme.Text,
        FontSize = 13.5,
        Padding = new Thickness(10, 0, 8, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private readonly TextBox _plain = new()
    {
        MaxLength = PasswordRules.MaximumLength,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Theme.Text,
        CaretBrush = Theme.Text,
        FontSize = 13.5,
        Padding = new Thickness(10, 0, 8, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };

    private readonly Border _eye;
    private readonly string _showKey;
    private readonly string _hideKey;
    private bool _showing;
    private bool _syncing;

    public PasswordField(string showKey = "tool.protect.show", string hideKey = "tool.protect.hide")
    {
        _showKey = showKey;
        _hideKey = hideKey;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Height = 38;

        _masked.PasswordChanged += (_, _) =>
        {
            if (_syncing)
                return;
            _syncing = true;
            _plain.Text = _masked.Password;
            _syncing = false;
            Changed?.Invoke();
        };
        _plain.TextChanged += (_, _) =>
        {
            if (_syncing)
                return;
            _syncing = true;
            _masked.Password = _plain.Text;
            _syncing = false;
            Changed?.Invoke();
        };

        var inner = new Grid();
        inner.Children.Add(_masked);
        inner.Children.Add(_plain);

        var frame = new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner,
        };
        SetColumn(frame, 0);
        Children.Add(frame);

        _eye = new Border
        {
            Width = 34,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\uE7B3",
                FontFamily = Theme.Icons,
                FontSize = 14,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ToolTipService.SetToolTip(_eye, T(_showKey));
        _eye.MouseEnter += (_, _) => _eye.Background = Theme.Hover;
        _eye.MouseLeave += (_, _) => _eye.Background = Brushes.Transparent;
        _eye.MouseLeftButtonUp += (_, _) => ToggleVisibility();
        SetColumn(_eye, 1);
        Children.Add(_eye);
    }

    public event Action? Changed;

    /// <summary>The value currently shown; callers must not keep it after the operation ends.</summary>
    public string Value => _showing ? _plain.Text : _masked.Password;

    public void Clear()
    {
        _syncing = true;
        _masked.Clear();
        _plain.Clear();
        _syncing = false;
    }

    public void FocusInput()
    {
        if (_showing)
        {
            _plain.Focus();
            _plain.CaretIndex = _plain.Text.Length;
        }
        else
        {
            _masked.Focus();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _masked.IsEnabled = enabled;
        _plain.IsEnabled = enabled;
        _eye.IsHitTestVisible = enabled;
        Opacity = enabled ? 1 : 0.55;
    }

    /// <summary>Switches between the masked and the visible control without losing the caret.</summary>
    public void ToggleVisibility()
    {
        var value = Value;
        _showing = !_showing;
        _syncing = true;
        if (_showing)
        {
            _plain.Text = value;
            _plain.Visibility = Visibility.Visible;
            _masked.Visibility = Visibility.Collapsed;
        }
        else
        {
            _masked.Password = value;
            _masked.Visibility = Visibility.Visible;
            _plain.Visibility = Visibility.Collapsed;
        }
        _syncing = false;

        ToolTipService.SetToolTip(_eye, T(_showing ? _hideKey : _showKey));
        FocusInput();
    }
}

/// <summary>Four segments and a word for the coarse password-strength estimate.</summary>
internal sealed class StrengthMeter : StackPanel
{
    private readonly Border[] _segments = new Border[4];
    private readonly TextBlock _label = new()
    {
        FontSize = 11.5,
        Foreground = Theme.Muted,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 0, 0),
    };

    public StrengthMeter()
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 6, 0, 0);

        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < _segments.Length; i++)
        {
            _segments[i] = new Border
            {
                Width = 44,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Track,
                Margin = new Thickness(0, 0, 4, 0),
            };
            bars.Children.Add(_segments[i]);
        }

        Children.Add(bars);
        Children.Add(_label);
        Set(string.Empty);
    }

    public void Set(string? password)
    {
        var strength = PasswordStrengthMeter.Evaluate(password);
        var filled = (int)strength + 1;
        var brush = strength switch
        {
            PasswordStrength.VeryWeak => Theme.Error,
            PasswordStrength.Strong => Theme.Success,
            _ => Theme.Accent,
        };
        for (var i = 0; i < _segments.Length; i++)
            _segments[i].Background = i < filled ? brush : Theme.Track;
        _label.Text = string.IsNullOrEmpty(password) ? string.Empty : T(PasswordStrengthMeter.LabelKey(strength));
    }
}

/// <summary>A switch row with a reset-able state, used for PDF owner permissions.</summary>
internal sealed class SwitchRow : DockPanel
{
    private readonly Border _track;
    private readonly System.Windows.Shapes.Ellipse _knob;
    private readonly TranslateTransform _translate = new();
    private bool _on;

    public SwitchRow(string label, Action<bool> changed)
    {
        Margin = new Thickness(0, 3, 0, 3);
        LastChildFill = true;

        _knob = new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = Theme.Muted,
        };
        _track = new Border
        {
            Width = 40,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.Muted,
            Cursor = Cursors.Hand,
            Child = _knob,
        };
        _knob.RenderTransform = _translate;
        _track.MouseLeftButtonUp += (_, _) =>
        {
            Set(!_on);
            changed(_on);
        };

        var text = Controls.Text(label, 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        SetDock(_track, Dock.Right);
        Children.Add(_track);
        Children.Add(text);

        Paint(false);
    }

    public bool IsOn => _on;

    public void Set(bool value)
    {
        _on = value;
        Paint(true);
    }

    private void Paint(bool animate)
    {
        _track.Background = _on ? Theme.AccentGradient : Brushes.Transparent;
        _track.BorderBrush = _on ? Brushes.Transparent : Theme.Muted;
        _knob.Fill = _on ? Brushes.White : Theme.Muted;
        var x = _on ? 23.0 : 3.0;
        if (animate)
        {
            _translate.BeginAnimation(
                TranslateTransform.XProperty,
                new System.Windows.Media.Animation.DoubleAnimation(x, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase(),
                });
        }
        else
        {
            _translate.X = x;
        }
    }
}

/// <summary>A progress bar, a status line and a Cancel button, hidden until an operation runs.</summary>
internal sealed class BusyRow : StackPanel
{
    private readonly ProgressBar _bar;
    private readonly TextBlock _status;
    private readonly Border _cancel;

    public BusyRow(string cancelText, Action cancel)
    {
        Visibility = Visibility.Collapsed;
        Margin = new Thickness(0, 10, 0, 0);

        _bar = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 100,
            Foreground = Theme.Accent,
            Background = Theme.Track,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6),
        };

        _status = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.Muted,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _cancel = Controls.Button(cancelText, cancel);
        _cancel.VerticalAlignment = VerticalAlignment.Center;

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_cancel, Dock.Right);
        row.Children.Add(_cancel);
        row.Children.Add(_status);

        Children.Add(_bar);
        Children.Add(row);
    }

    public void Show(bool indeterminate, string status)
    {
        Visibility = Visibility.Visible;
        _bar.IsIndeterminate = indeterminate;
        _bar.Value = 0;
        _status.Text = status;
    }

    public void Report(double fraction)
    {
        if (_bar.IsIndeterminate)
            return;
        _bar.Value = Math.Clamp(fraction, 0, 1) * 100;
    }

    public void Hide() => Visibility = Visibility.Collapsed;
}

/// <summary>Small styled sub-controls shared by both views.</summary>
internal static class PasswordViewParts
{
    public static TextBlock Caption(string key) => new()
    {
        Text = T(key),
        FontSize = 11.5,
        Foreground = Theme.Muted,
        Margin = new Thickness(0, 10, 0, 4),
    };

    public static TextBlock Note(string key) => new()
    {
        Text = T(key),
        FontSize = 11.5,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        LineHeight = 16,
    };

    public static TextBlock Error() => new()
    {
        FontSize = 12,
        Foreground = Theme.Error,
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0),
    };
}
