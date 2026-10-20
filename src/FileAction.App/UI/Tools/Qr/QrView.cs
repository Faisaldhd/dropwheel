using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using WpfImage = System.Windows.Controls.Image;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Qr;

/// <summary>
/// The QR window content: the image, what the code turned out to be, the decoded text (selectable), the
/// parsed details, and Copy / Open. Decoding runs on a background thread; the UI thread only paints.
/// </summary>
internal sealed class QrView
{
    /// <summary>Bigger images are downsampled before decoding: a QR stays readable far below this.</summary>
    private const int DecodeMaxSide = 1200;

    private readonly Task<QrLoaded> _load;
    private readonly TaskCompletionSource<MediaSize?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Border _badge;
    private readonly TextBlock _badgeText = new() { FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text };
    private readonly TextBlock _headline;
    private readonly TextBlock _feedback;
    private readonly StackPanel _fields;
    private readonly TextBox _result;
    private readonly Border _copyButton;
    private readonly Border _openButton;
    private readonly Border _placeholder;
    private readonly WpfImage _imageView = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };

    private QrPayload? _payload;
    private bool _noCode;

    public QrView(FileItem item)
    {
        _badge = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 2, 8, 3),
            Background = Theme.AccentSoft,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _badgeText,
        };

        _headline = new TextBlock
        {
            Text = T("tool.qr.reading"),
            FontSize = 13,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 0, 0),
        };

        _fields = new StackPanel { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };

        _result = new TextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Theme.Card,
            Foreground = Theme.Text,
            BorderBrush = Theme.PanelBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8),
            FontFamily = Theme.UiFont,
            FontSize = 13,
            MaxHeight = 104,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        _copyButton = Controls.Button(T("tool.qr.copy"), Copy, glyph: Glyphs.Copy);
        _openButton = Controls.Button(T("tool.qr.open"), Open, glyph: Glyphs.Link);
        foreach (var button in new[] { _copyButton, _openButton })
        {
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Visibility = Visibility.Collapsed;
        }
        _copyButton.Margin = new Thickness(0, 0, 8, 0);
        _openButton.Margin = new Thickness(0, 0, 8, 0);

        _feedback = new TextBlock
        {
            FontSize = 12,
            Foreground = Theme.Muted,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        _placeholder = new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(10),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new ProgressBar { IsIndeterminate = true, Width = 120, Height = 4 },
                    new TextBlock
                    {
                        Text = T("tool.qr.reading"),
                        FontSize = 12,
                        Foreground = Theme.Muted,
                        Margin = new Thickness(0, 10, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };

        // Magick decodes, ZXing reads and the PNG is turned into a frozen WPF bitmap: all off the UI thread.
        _load = Task.Run(() => Load(item.Path));
    }

    public ToolContent Build()
    {
        // The window's title and bottom bars already inset their content by 18 DIP; the view matches them.
        var root = new Grid { Margin = new Thickness(18, 0, 18, 0) };
        for (var index = 0; index < 5; index++)
            root.RowDefinitions.Add(new RowDefinition { Height = index == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var status = BuildStatus();
        var preview = BuildPreview();
        var actions = BuildActions();
        Grid.SetRow(status, 0);
        Grid.SetRow(preview, 1);
        Grid.SetRow(_fields, 2);
        Grid.SetRow(_result, 3);
        Grid.SetRow(actions, 4);
        root.Children.Add(status);
        root.Children.Add(preview);
        root.Children.Add(_fields);
        root.Children.Add(_result);
        root.Children.Add(actions);

        return new ToolContent(root, Reset, ApplyAsync, _ready.Task, ActivateAsync);
    }

    /// <summary>Shows the decoded result, on the UI thread, once the background work has finished.</summary>
    public async Task ActivateAsync()
    {
        try
        {
            var loaded = await _load;
            _imageView.Source = loaded.Preview;
            _imageView.Visibility = Visibility.Visible;
            _placeholder.Visibility = Visibility.Collapsed;
            _payload = loaded.Payload;
            if (_payload is null)
                ShowNoCode();
            else
                ShowPayload(_payload);
            _ready.TrySetResult(loaded.Width > 0 && loaded.Height > 0 ? new MediaSize(loaded.Width, loaded.Height) : null);
        }
        catch (Exception ex)
        {
            ShowInvalid(ex.Message);
            _ready.TrySetResult(null);
        }
    }

    private FrameworkElement BuildStatus()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_badge, Dock.Left);
        row.Children.Add(_badge);
        row.Children.Add(_headline);
        return row;
    }

    private FrameworkElement BuildPreview()
    {
        var surface = new Grid { Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true };
        surface.Children.Add(_imageView);
        surface.Children.Add(_placeholder);
        return new Border
        {
            Background = Theme.Card,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(6),
            Child = surface,
        };
    }

    private FrameworkElement BuildActions()
    {
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        buttons.Children.Add(_copyButton);
        buttons.Children.Add(_openButton);

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Left);
        row.Children.Add(buttons);
        row.Children.Add(_feedback);
        return row;
    }

    private void ShowPayload(QrPayload payload)
    {
        _badge.Visibility = Visibility.Visible;
        _badgeText.Text = T(payload.KindKey);

        _headline.Text = Bidi.Value(payload.Title);
        _headline.Foreground = Theme.Muted;
        _headline.Visibility = string.IsNullOrEmpty(payload.Title) ? Visibility.Collapsed : Visibility.Visible;

        _fields.Children.Clear();
        foreach (var field in payload.Fields)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var label = new TextBlock
            {
                Text = T(field.LabelKey),
                FontSize = 12,
                Foreground = Theme.Muted,
                Width = 96,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Top,
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(new TextBlock
            {
                Text = Bidi.Value(field.Value),
                FontSize = 12.5,
                Foreground = Theme.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            });
            _fields.Children.Add(row);
        }
        _fields.Visibility = _fields.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _result.Text = payload.Text;
        _result.Visibility = string.IsNullOrEmpty(payload.Text) ? Visibility.Collapsed : Visibility.Visible;
        _result.Select(0, 0);
        _result.ScrollToHome();

        ShowAction(_copyButton, !string.IsNullOrEmpty(payload.Text));
        ShowAction(_openButton, payload.CanOpen);
        _feedback.Text = string.Empty;
    }

    private void ShowNoCode()
    {
        _noCode = true;
        _badge.Visibility = Visibility.Collapsed;
        _headline.Text = T("tool.qr.empty");
        _headline.Foreground = Theme.Error;
        _headline.Visibility = Visibility.Visible;
        _fields.Children.Clear();
        _fields.Visibility = Visibility.Collapsed;
        _result.Visibility = Visibility.Collapsed;
        ShowAction(_copyButton, false);
        ShowAction(_openButton, false);
        _feedback.Text = T("tool.qr.empty.hint");
        _feedback.Foreground = Theme.Muted;
    }

    private void ShowInvalid(string? detail)
    {
        _noCode = false;
        _imageView.Visibility = Visibility.Collapsed;
        _placeholder.Visibility = Visibility.Visible;
        _placeholder.Child = BuildMessage(T("tool.qr.invalid"), detail);
        _badge.Visibility = Visibility.Collapsed;
        // The message sits in the preview card itself, so the status line stays out of the way.
        _headline.Text = T("tool.qr.invalid");
        _headline.Visibility = Visibility.Collapsed;
        _fields.Children.Clear();
        _fields.Visibility = Visibility.Collapsed;
        _result.Visibility = Visibility.Collapsed;
        ShowAction(_copyButton, false);
        ShowAction(_openButton, false);
        _feedback.Text = string.Empty;
    }

    private static FrameworkElement BuildMessage(string message, string? detail)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(18),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Theme.Text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
        });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11.5,
                Foreground = Theme.Muted,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = System.Windows.TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        return panel;
    }

    private static void ShowAction(Border button, bool enabled)
    {
        button.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        button.IsEnabled = enabled;
        button.Opacity = enabled ? 1.0 : 0.45;
    }

    /// <summary>Clears the last message and the selection; the decoded result itself never changes.</summary>
    private void Reset()
    {
        if (_payload is null)
        {
            if (_noCode)
                ShowNoCode();
            else
                _feedback.Text = string.Empty;
            return;
        }
        _result.Select(0, 0);
        _result.ScrollToHome();
        ShowPayload(_payload);
    }

    /// <summary>The window's primary action: copy the text and close.</summary>
    private Task<string?> ApplyAsync()
    {
        if (!string.IsNullOrEmpty(_payload?.Text) && !TryCopy(_payload.Text))
            throw new InvalidOperationException(T("tool.qr.copyFailed"));
        return Task.FromResult<string?>(null);
    }

    private void Copy()
    {
        if (_payload is null || string.IsNullOrEmpty(_payload.Text) || !TryCopy(_payload.Text))
        {
            _feedback.Text = T("tool.qr.copyFailed");
            _feedback.Foreground = Theme.Error;
            return;
        }
        _feedback.Text = T("tool.qr.copied");
        _feedback.Foreground = Theme.Success;
    }

    /// <summary>Opens the browser, and only ever for a link <see cref="QrSafety"/> already accepted.</summary>
    private void Open()
    {
        if (QrSafety.HttpUrl(_payload?.Url) is not { } url)
        {
            _feedback.Text = T("tool.qr.blocked");
            _feedback.Foreground = Theme.Error;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _feedback.Text = string.Empty;
        }
        catch (Exception)
        {
            _feedback.Text = T("tool.qr.openFailed");
            _feedback.Foreground = Theme.Error;
        }
    }

    private static bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            // Another app can hold the clipboard open; the window then says so instead of throwing.
            return false;
        }
    }

    /// <summary>
    /// Reads the image, keeps its pixel size for the adaptive window and decodes a copy of at most
    /// <see cref="DecodeMaxSide"/> pixels. Runs on a background thread.
    /// </summary>
    private static QrLoaded Load(string path)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        var width = (int)image.Width;
        var height = (int)image.Height;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("The image has no pixels.");

        var longSide = Math.Max(width, height);
        if (longSide > DecodeMaxSide)
        {
            var scale = (double)DecodeMaxSide / longSide;
            image.Resize(new MagickGeometry((uint)Math.Max(1, width * scale), (uint)Math.Max(1, height * scale)));
        }

        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var pixels = ToBgra32(bitmap, out var pixelWidth, out var pixelHeight);
        var text = QrDecoder.Decode(pixels, pixelWidth, pixelHeight);
        return new QrLoaded(bitmap, width, height, text is null ? null : QrPayload.Parse(text));
    }

    /// <summary>Copies the bitmap out as BGRA32, the layout the decoder reads.</summary>
    private static byte[] ToBgra32(BitmapSource source, out int width, out int height)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        if (bgra.CanFreeze && !bgra.IsFrozen)
            bgra.Freeze();

        width = bgra.PixelWidth;
        height = bgra.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>The decoded preview, the source pixel size and what the code said (null when it said nothing).</summary>
    private sealed record QrLoaded(BitmapSource Preview, int Width, int Height, QrPayload? Payload);
}
