using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.Linux.UI;

/// <summary>Asks for the values an operation needs (e.g. trim start and end).</summary>
public sealed class ParameterWindow : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new();
    private readonly TaskCompletionSource<bool> _result = new();

    public ParameterWindow(Operation op, IReadOnlyList<FileItem> items)
    {
        Palette.Refresh();
        Title = op.Label;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = Palette.Page;
        Foreground = Palette.Text;
        Icon = AppIcon.Window;

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 22), MinWidth = 320, Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = items.Count == 1 ? items[0].Name : Items(items.Count), FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var p in op.Parameters)
        {
            stack.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 2) });
            var box = new TextBox { Text = p.DefaultValue };
            _boxes[p.Key] = box;
            stack.Children.Add(box);
            if (p.Hint is not null)
                stack.Children.Add(new TextBlock { Text = p.Hint, Opacity = 0.65, FontSize = 11.5 });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(Widgets.Button(T("Spustit", "Run"), () => Finish(true), primary: true));
        buttons.Children.Add(Widgets.Button(T("Zrušit", "Cancel"), () => Finish(false)));
        stack.Children.Add(buttons);
        Content = stack;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Finish(true);
            else if (e.Key == Key.Escape)
                Finish(false);
        };
        Opened += (_, _) => _boxes.Values.FirstOrDefault()?.Focus();
        Closed += (_, _) => _result.TrySetResult(false);
    }

    /// <summary>Shows the window; true when the user pressed Run.</summary>
    public Task<bool> AskAsync()
    {
        Show();
        Activate();
        return _result.Task;
    }

    private void Finish(bool ok)
    {
        _result.TrySetResult(ok);
        Close();
    }

    public IReadOnlyDictionary<string, string> Values => _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? "");
}
