using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace FileAction.Linux.UI;

/// <summary>Small drawn controls matching the Windows app (pill buttons).</summary>
internal static class Widgets
{
    public static Border Button(string text, Action click, bool primary = false)
    {
        var button = new Border
        {
            Background = primary ? Palette.AccentGradient : Palette.Track,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 6, 14, 7),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = primary ? Palette.TextOnAccent : Palette.Text,
            },
        };
        button.PointerEntered += (_, _) => button.Opacity = 0.88;
        button.PointerExited += (_, _) => button.Opacity = 1;
        button.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return button;
    }
}
