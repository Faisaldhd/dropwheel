using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI;

/// <summary>Asks for the values an operation needs (e.g. trim start and end).</summary>
internal sealed class ParameterDialog : Window
{
    private readonly Dictionary<string, TextBox> _boxes = new();

    /// <param name="tools">Used to find FFmpeg for a video preview; a fresh locator is made when omitted.</param>
    public ParameterDialog(Operation op, IReadOnlyList<FileItem> items, ToolLocator? tools = null)
    {
        Title = op.Label;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        MinWidth = 360;
        Theme.Refresh();
        Background = Theme.Page;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;

        var stack = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        stack.Children.Add(new TextBlock
        {
            Text = items.Count == 1 ? items[0].Name : Items(items.Count),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
        });

        // A preview of the file being processed, above the options. It stays collapsed (and does no
        // work) when the operation has none or the file cannot be read.
        PreviewPane? preview = null;
        if (op.Preview != PreviewKind.None && items.Count > 0)
        {
            preview = new PreviewPane(op.Preview, items[0].Path, tools);
            stack.Children.Add(preview);
        }

        foreach (var p in op.Parameters)
        {
            stack.Children.Add(new TextBlock { Text = p.Label, Margin = new Thickness(0, 6, 0, 4) });
            var box = new TextBox
            {
                Text = p.DefaultValue,
                MinWidth = 300,
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 14,
                Background = Theme.Card,
                Foreground = Theme.Text,
                CaretBrush = Theme.Text,
                BorderBrush = Theme.PanelBorder,
            };
            _boxes[p.Key] = box;
            stack.Children.Add(box);
            if (p.Hint is not null)
                stack.Children.Add(new TextBlock { Text = p.Hint, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) });
        }

        var ok = Controls.Button(T("Spustit", "Run"), () => DialogResult = true, primary: true);
        ok.Margin = new Thickness(0, 0, 8, 0);
        var cancel = Controls.Button(T("Zrušit", "Cancel"), () => DialogResult = false);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                DialogResult = true;
            else if (e.Key == Key.Escape)
                DialogResult = false;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Content = stack;

        Loaded += (_, _) =>
        {
            ForegroundHelper.Force(this);
            if (preview is not null)
                _ = preview.StartAsync();
            if (_boxes.Values.FirstOrDefault() is { } first)
            {
                first.Focus();
                first.SelectAll();
                Keyboard.Focus(first);
            }
        };
        Closed += (_, _) => preview?.Stop();
    }

    public IReadOnlyDictionary<string, string> Values => _boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text);
}
