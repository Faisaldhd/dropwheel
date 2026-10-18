using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static FileAction.Core.Loc;

namespace FileAction.Linux.UI;

/// <summary>Progress cards in the bottom-right corner.</summary>
public sealed class JobsWindow : Window
{
    private readonly StackPanel _cards = new();

    public JobsWindow()
    {
        Title = T("DropWheel – průběh", "DropWheel – progress");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Icon = AppIcon.Window;
        Content = _cards;
        SizeChanged += (_, _) => PlaceInCorner();
    }

    /// <summary>Raised when the last card was removed.</summary>
    public event Action? Emptied;

    public JobCard Add(string title, string detail, Action cancel)
    {
        Palette.Refresh();
        var card = new JobCard(title, detail, cancel, Remove);
        _cards.Children.Insert(0, card);
        if (!IsVisible)
            Show();
        PlaceInCorner();
        return card;
    }

    private void Remove(JobCard card)
    {
        _cards.Children.Remove(card);
        if (_cards.Children.Count == 0)
        {
            Hide();
            Emptied?.Invoke();
        }
    }

    private void PlaceInCorner()
    {
        if (Screens.Primary?.WorkingArea is not { } area)
            return;
        var scale = Screens.Primary.Scaling;
        Position = new PixelPoint(
            area.Right - (int)((Bounds.Width + 8) * scale),
            area.Bottom - (int)((Bounds.Height + 8) * scale));
    }

    public Panel Cards => _cards;
}

public sealed class JobCard : Border
{
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(2), Background = Palette.AccentGradient, HorizontalAlignment = HorizontalAlignment.Left, Width = 48 };
    private readonly Grid _bar = new() { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Action<JobCard> _remove;
    private double _value;
    private bool _done;

    public JobCard(string title, string detail, Action cancel, Action<JobCard> remove)
    {
        _remove = remove;
        Background = Palette.Panel;
        BorderBrush = Palette.PanelBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Margin = new Thickness(12, 6, 12, 6);
        Padding = new Thickness(16, 14, 12, 14);
        BoxShadow = new BoxShadows(new BoxShadow { Blur = 22, OffsetY = 3, Color = Color.FromArgb(Palette.IsDark ? (byte)140 : (byte)56, 0, 0, 0) });

        var close = new TextBlock { Text = "✕", FontSize = 12, Foreground = Palette.Muted, Padding = new Thickness(6), Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        close.PointerReleased += (_, _) =>
        {
            if (!_done)
                cancel();
            _remove(this);
        };
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text, TextTrimming = TextTrimming.CharacterEllipsis });

        _bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Palette.Track });
        _bar.Children.Add(_fill);
        _bar.SizeChanged += (_, _) =>
        {
            if (_value > 0)
                _fill.Width = _bar.Bounds.Width * _value;
        };
        _buttons.Children.Add(Widgets.Button(T("Zrušit", "Cancel"), cancel));

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Palette.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        body.Children.Add(_bar);
        body.Children.Add(_status);
        body.Children.Add(_buttons);
        Child = body;
    }

    public void Report(double value)
    {
        if (_done || value <= 0)
            return;
        _value = Math.Clamp(value, 0, 1);
        _fill.Width = _bar.Bounds.Width * _value;
    }

    public void Complete(IReadOnlyList<string> outputs, string? warning, string? note = null)
    {
        _done = true;
        Full(warning is null ? Palette.Success : Palette.Error);
        _status.IsVisible = true;
        _status.Foreground = warning is null ? Palette.Muted : Palette.Error;
        _status.Text = (warning ?? (outputs.Count == 1 ? T("Hotovo: ", "Done: ") + System.IO.Path.GetFileName(outputs[0]) : T($"Hotovo: {outputs.Count} výstupů", $"Done: {outputs.Count} outputs")))
            + (warning is null && note is not null ? Environment.NewLine + note : "");
        _buttons.Children.Clear();
        if (outputs.Count > 0)
        {
            var first = outputs[0];
            if (File.Exists(first) && outputs.Count == 1)
                _buttons.Children.Add(Widgets.Button(T("Otevřít", "Open"), () => Desktop.Open(first), primary: true));
            _buttons.Children.Add(Widgets.Button(T("Zobrazit ve složce", "Show in folder"), () => Desktop.Reveal(first)));
        }
        if (warning is null)
            DispatcherTimer.RunOnce(() => _remove(this), TimeSpan.FromSeconds(8));
    }

    public void Fail(string message)
    {
        _done = true;
        Full(Palette.Error);
        _status.IsVisible = true;
        _status.Foreground = Palette.Error;
        _status.Text = message;
        _buttons.Children.Clear();
        _buttons.Children.Add(Widgets.Button(T("Zavřít", "Close"), () => _remove(this)));
    }

    public void Cancelled()
    {
        _done = true;
        _remove(this);
    }

    private void Full(IBrush brush)
    {
        _value = 1;
        _fill.Background = brush;
        _fill.Width = double.NaN;
        _fill.HorizontalAlignment = HorizontalAlignment.Stretch;
    }
}
