using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.UI;
using FileAction.Core;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The reveal state of a password field: hidden by default, the mirror text exists only while revealed,
/// and hiding clears it. Kept separate from the controls so the reset behaviour is testable without a UI.
/// </summary>
internal sealed class SecretRevealState
{
    public bool Revealed { get; private set; }

    /// <summary>The visible mirror text; empty whenever the field is hidden.</summary>
    public string Mirror { get; private set; } = string.Empty;

    public void Reveal(string secret)
    {
        Revealed = true;
        Mirror = secret;
    }

    /// <summary>Hides the field and clears the mirror text.</summary>
    public void Hide()
    {
        Revealed = false;
        Mirror = string.Empty;
    }

    /// <summary>Closing a dialog resets the field to hidden.</summary>
    public void Reset() => Hide();
}

/// <summary>A password box with an eye toggle, built from theme brushes only and mirrored for Arabic.</summary>
internal sealed class SecretField : Grid
{
    private readonly PasswordBox _hidden = new();
    private readonly TextBox _shown = new();
    private readonly Border _eye;
    private readonly SecretRevealState _state = new();

    public SecretField(string hint, bool showEye = true)
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        foreach (var box in new Control[] { _hidden, _shown })
        {
            box.Padding = new Thickness(10, 7, 10, 7);
            box.Background = Theme.Card;
            box.Foreground = Theme.Text;
            box.BorderBrush = Theme.PanelBorder;
            box.BorderThickness = new Thickness(1);
            box.FontSize = 13;
            box.ToolTip = hint;
        }
        _hidden.PasswordChanged += (_, _) =>
        {
            if (_state.Revealed)
                _shown.Text = _hidden.Password;
            Changed?.Invoke();
        };
        _shown.TextChanged += (_, _) =>
        {
            if (_state.Revealed)
                _hidden.Password = _shown.Text;
            Changed?.Invoke();
        };
        _shown.Visibility = Visibility.Collapsed;
        SetColumn(_hidden, 0);
        SetColumn(_shown, 0);
        Children.Add(_hidden);
        Children.Add(_shown);

        _eye = new Border
        {
            Width = 34,
            CornerRadius = new CornerRadius(7),
            Background = Theme.Track,
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            Focusable = true,
            Visibility = showEye ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "\uE7B3",
                FontFamily = Theme.Icons,
                FontSize = 13,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = T("tool.lock.show"),
        };
        _eye.MouseLeftButtonUp += (_, _) =>
        {
            _eye.Focus();
            Toggle();
        };
        SetColumn(_eye, 1);
        Children.Add(_eye);

        // Leaving the whole field hides the mirror; moving focus to the eye keeps it visible for toggling.
        IsKeyboardFocusWithinChanged += (_, _) => Dispatcher.BeginInvoke((Action)(() =>
        {
            if (!IsKeyboardFocusWithin && !_eye.IsKeyboardFocusWithin)
                HideReveal();
        }));
        Unloaded += (_, _) => HideReveal();
    }

    public event Action? Changed;

    public string Text => _state.Revealed ? _shown.Text : _hidden.Password;

    public void Clear()
    {
        _hidden.Clear();
        _shown.Clear();
        _state.Hide();
    }

    public void FocusInput()
    {
        if (_state.Revealed)
            _shown.Focus();
        else
            _hidden.Focus();
    }

    /// <summary>Hides the field, copies a revealed value back and clears the mirror text.</summary>
    public void HideReveal()
    {
        if (_state.Revealed)
        {
            _hidden.Password = _shown.Text;
            _shown.Clear();
            _shown.Visibility = Visibility.Collapsed;
            _hidden.Visibility = Visibility.Visible;
        }
        _state.Hide();
        PaintEye(false);
    }

    private void Toggle()
    {
        if (_state.Revealed)
        {
            HideReveal();
            _hidden.Focus();
            return;
        }
        _state.Reveal(_hidden.Password);
        _shown.Text = _state.Mirror;
        _shown.Visibility = Visibility.Visible;
        _hidden.Visibility = Visibility.Collapsed;
        _shown.CaretIndex = _shown.Text.Length;
        _shown.Focus();
        PaintEye(true);
    }

    private void PaintEye(bool revealed)
    {
        if (_eye.Child is TextBlock glyph)
            glyph.Text = revealed ? "\uE7B3" : "\uE890";
        _eye.ToolTip = T(revealed ? "tool.lock.hide" : "tool.lock.show");
    }
}
/// <summary>The strength meter under a password box: four segments plus a short label.</summary>
internal sealed class StrengthMeter : StackPanel
{
    private readonly Border[] _segments = new Border[4];
    private readonly TextBlock _label;

    public StrengthMeter()
    {
        Orientation = Orientation.Horizontal;
        Margin = new Thickness(0, 6, 0, 0);
        for (var i = 0; i < _segments.Length; i++)
        {
            _segments[i] = new Border
            {
                Width = 34,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = Theme.Track,
                Margin = new Thickness(0, 0, 4, 0),
            };
            Children.Add(_segments[i]);
        }
        _label = new TextBlock
        {
            FontSize = 11.5,
            Foreground = Theme.LockMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Text = T("tool.lock.strength.empty"),
        };
        Children.Add(_label);
    }

    public void Update(string? password)
    {
        var score = LockSecrets.Strength(password);
        for (var i = 0; i < _segments.Length; i++)
        {
            _segments[i].Background = i < score
                ? score <= 1 ? Theme.Error : score == 2 ? Theme.LockBadgeHighlight : Theme.Success
                : Theme.Track;
        }
        _label.Text = T(score switch
        {
            0 => "tool.lock.strength.empty",
            1 => "tool.lock.strength.weak",
            2 => "tool.lock.strength.fair",
            3 => "tool.lock.strength.good",
            _ => "tool.lock.strength.strong",
        });
    }
}

/// <summary>Shared look for the protection windows.</summary>
internal static class LockUi
{
    public static Border Card(UIElement content, Thickness? padding = null) => new()
    {
        Background = Theme.LockCard,
        BorderBrush = Theme.LockCardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = padding ?? new Thickness(14, 12, 14, 12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = content,
    };

    public static Border Pill(string text, Brush background, Brush foreground) => new()
    {
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(8, 2, 8, 3),
        Background = background,
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = foreground,
        },
    };

    /// <summary>Configures one window with the shared chrome and returns the body panel to fill.</summary>
    public static StackPanel Configure(Window window, string title, double width, double height)
    {
        Theme.Refresh();
        window.Title = title;
        window.Width = width;
        window.Height = height;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.Topmost = true;
        window.ShowInTaskbar = false;
        window.WindowStartupLocation = window.Owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        window.FlowDirection = LanguageManager.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.FontFamily = Theme.UiFont;
        window.Foreground = Theme.Text;

        var titleBar = new DockPanel { Margin = new Thickness(16, 14, 16, 8) };
        var close = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Theme.Track,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE711",
                FontFamily = Theme.Icons,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Text,
            },
            ToolTip = T("action.cancel"),
        };
        close.MouseLeftButtonUp += (_, _) => window.Close();
        // LTR puts Close at the top-right; FlowDirection.RightToLeft mirrors it to the top-left.
        DockPanel.SetDock(close, Dock.Right);
        titleBar.Children.Add(close);
        titleBar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Theme.Text,
        });

        var body = new StackPanel { Margin = new Thickness(16, 4, 16, 16) };
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(titleBar);
        grid.Children.Add(scroll);

        var panelColor = (Theme.Panel as SolidColorBrush)?.Color ?? Colors.Black;
        window.Content = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0xF7, panelColor.R, panelColor.G, panelColor.B)),
            BorderBrush = Theme.LockCardBorder,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Child = grid,
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;
            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };
        return body;
    }
}