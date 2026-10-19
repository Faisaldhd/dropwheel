using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileAction.App.UI;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Image.Frame;

/// <summary>Ask for one custom colour with three channel sliders and a hex box.</summary>
internal static class AddBackgroundColorDialog
{
    /// <summary>The chosen colour, or null when the user cancelled.</summary>
    public static BackgroundColor? Pick(Window? owner, BackgroundColor current)
    {
        var window = new AddBackgroundColorWindow(current);
        try
        {
            if (owner is not null && !ReferenceEquals(owner, window))
                window.Owner = owner;
        }
        catch (InvalidOperationException)
        {
            // An owner that is already closing is not worth failing the dialog for.
        }
        return window.ShowDialog() == true ? window.Chosen : null;
    }
}

/// <summary>
/// A small modal colour picker: a live swatch, a hex box and red/green/blue sliders. It follows the app
/// theme and reading direction and never leaves the tool window, so the rest of the UI stays untouched.
/// </summary>
internal sealed class AddBackgroundColorWindow : Window
{
    private readonly int[] _channels = new int[3];
    private readonly Border _swatch;
    private readonly TextBox _hex;
    private bool _syncing;

    public AddBackgroundColorWindow(BackgroundColor current)
    {
        Theme.Refresh();
        Title = T("tool.addbackground.color.title");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        UiSetup.Configure(this);

        Chosen = current;
        _channels[0] = current.R;
        _channels[1] = current.G;
        _channels[2] = current.B;

        _swatch = new Border
        {
            Width = 132,
            Height = 46,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Paint(current)),
        };

        _hex = new TextBox
        {
            Width = 96,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Card,
            Foreground = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
        };
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                CommitHex();
        };
        _hex.LostFocus += (_, _) => CommitHex();

        Content = Build();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        SyncFromChannels();
    }

    /// <summary>The colour the user confirmed; only meaningful when the dialog returned true.</summary>
    public BackgroundColor Chosen { get; private set; }

    private FrameworkElement Build()
    {
        var title = new TextBlock
        {
            Text = T("tool.addbackground.color.title"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.Text,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var boxes = new Grid();
        boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        boxes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_swatch, 0);
        boxes.Children.Add(_swatch);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        hexRow.Children.Add(new TextBlock
        {
            Text = T("tool.addbackground.color.hex"),
            FontSize = 11.5,
            Foreground = Theme.Muted,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        hexRow.Children.Add(_hex);
        Grid.SetColumn(hexRow, 1);
        boxes.Children.Add(hexRow);

        var sliders = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        sliders.Children.Add(SliderRow("tool.addbackground.color.red", 0));
        sliders.Children.Add(SliderRow("tool.addbackground.color.green", 1));
        sliders.Children.Add(SliderRow("tool.addbackground.color.blue", 2));

        var cancel = Controls.Button(T("tool.addbackground.color.cancel"), Close);
        var ok = Controls.Button(T("tool.addbackground.color.ok"), Confirm, primary: true);
        var buttons = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Left);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var column = new StackPanel { Margin = new Thickness(22) };
        column.Children.Add(title);
        column.Children.Add(boxes);
        column.Children.Add(sliders);
        column.Children.Add(buttons);

        return new Border
        {
            Background = Theme.Panel,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Effect = Theme.Shadow(22),
            Child = column,
            Margin = new Thickness(14),
        };
    }

    private FrameworkElement SliderRow(string key, int channel)
    {
        var label = new TextBlock
        {
            Text = T(key),
            Width = 62,
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = Controls.Slider(0, 255, _channels[channel],
            value => SetChannel(channel, value),
            committed: () => { });
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 2) };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(slider, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(slider);
        return row;
    }

    private void SetChannel(int channel, int value)
    {
        if (_syncing)
            return;
        _channels[channel] = Math.Clamp(value, 0, 255);
        SyncFromChannels();
    }

    private void SyncFromChannels()
    {
        _syncing = true;
        var color = new BackgroundColor((byte)_channels[0], (byte)_channels[1], (byte)_channels[2]);
        _swatch.Background = new SolidColorBrush(Paint(color));
        _hex.Text = $"#{_channels[0]:X2}{_channels[1]:X2}{_channels[2]:X2}";
        _syncing = false;
    }

    private void CommitHex()
    {
        if (_syncing)
            return;
        if (!TryParseHex(_hex.Text, out var color))
        {
            SyncFromChannels();
            return;
        }
        _channels[0] = color.R;
        _channels[1] = color.G;
        _channels[2] = color.B;
        SyncFromChannels();
        // The sliders were built with the old values; rebuild the colour readout only, the thumbs
        // still show the previous channel but the chosen colour is correct and updated on confirm.
        RebuildSliders();
    }

    private void RebuildSliders()
    {
        // A slider's thumb cannot be moved programmatically, so the rows are rebuilt from the channels.
        if (Content is Border { Child: StackPanel column } && column.Children.Count >= 3
            && column.Children[2] is StackPanel sliders)
        {
            sliders.Children.Clear();
            sliders.Children.Add(SliderRow("tool.addbackground.color.red", 0));
            sliders.Children.Add(SliderRow("tool.addbackground.color.green", 1));
            sliders.Children.Add(SliderRow("tool.addbackground.color.blue", 2));
        }
    }

    private void Confirm()
    {
        Chosen = new BackgroundColor((byte)_channels[0], (byte)_channels[1], (byte)_channels[2]);
        DialogResult = true;
    }

    private static bool TryParseHex(string? text, out BackgroundColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
            return false;
        color = new BackgroundColor((byte)((number >> 16) & 0xFF), (byte)((number >> 8) & 0xFF), (byte)(number & 0xFF));
        return true;
    }

    private static Color Paint(BackgroundColor color) => Color.FromRgb(color.R, color.G, color.B);
}
