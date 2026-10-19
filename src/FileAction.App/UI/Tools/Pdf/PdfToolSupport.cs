using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using static FileAction.Core.Loc;
using WpfImage = System.Windows.Controls.Image;

namespace FileAction.App.UI.Tools.Pdf;

/// <summary>
/// Rebuilds open PDF tool views when Windows switches between the light and dark app theme. Theme returns
/// fresh frozen brushes on Refresh, so a view that keeps its old elements would keep the old palette;
/// rebuilding the body picks up the new one. Handlers are held weakly, so a closed window is collected.
/// </summary>
internal static class PdfThemeWatcher
{
    private static readonly List<WeakReference<Action>> Handlers = [];
    private static bool _hooked;

    public static void Subscribe(Action repaint)
    {
        lock (Handlers)
        {
            Handlers.RemoveAll(reference => !reference.TryGetTarget(out _));
            Handlers.Add(new WeakReference<Action>(repaint));
        }
        EnsureHooked();
    }

    private static void EnsureHooked()
    {
        if (_hooked)
            return;
        _hooked = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (sender, args) =>
        {
            if (args.Category is not (Microsoft.Win32.UserPreferenceCategory.General
                or Microsoft.Win32.UserPreferenceCategory.VisualStyle
                or Microsoft.Win32.UserPreferenceCategory.Color))
                return;

            var application = Application.Current;
            if (application is null)
                return;

            try
            {
                application.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Theme.Refresh();
                    var handlers = new List<Action>();
                    lock (Handlers)
                    {
                        foreach (var reference in Handlers)
                            if (reference.TryGetTarget(out var handler))
                                handlers.Add(handler);
                        Handlers.RemoveAll(reference => !reference.TryGetTarget(out _));
                    }
                    foreach (var handler in handlers)
                        handler();
                }));
            }
            catch (InvalidOperationException)
            {
                // The dispatcher is already shutting down; nothing left to repaint.
            }
        };
    }
}

/// <summary>Where the two PDF tools write: the drop folder when it is usable, else next to the source.</summary>
internal static class PdfToolOutput
{
    /// <summary>The folder this drop resolved (2.2.3 rules), or the source folder as the fallback.</summary>
    public static string DirectoryFor(FileItem item, DropTargets? targets)
    {
        var detected = OutputTarget.Resolve(item.Path, targets?.UnderCursor, Directory.Exists, OutputTarget.CanWrite);
        if (!detected.UsesDetectedFolder && targets?.Remembered is { } remembered)
        {
            var fallback = OutputTarget.Resolve(item.Path, remembered, Directory.Exists, OutputTarget.CanWrite);
            if (fallback.UsesDetectedFolder)
                return fallback.Directory!;
        }
        return detected.Directory
            ?? OutputTarget.SourceFolder(item.Path)
            ?? Environment.CurrentDirectory;
    }
}

/// <summary>
/// Low-cost thumbnails for the PDF tools: PDFium renders a PDF page at a low DPI, Magick downsamples an
/// image first. Both are plain background work; the caller decides when to run them.
/// </summary>
internal static class PdfThumbnails
{
    /// <summary>Enough pixels for a 112 DIP tile, without rendering the page at print quality.</summary>
    public const int PdfDpi = 24;

    /// <summary>Renders one PDF page (0-based) and decodes it near <paramref name="decodeWidth"/>.</summary>
    public static BitmapSource FromPdf(string path, int pageIndex, int decodeWidth) =>
        FromPdf(File.ReadAllBytes(path), pageIndex, decodeWidth);

    /// <summary>Renders one page from bytes the caller already read, e.g. when rendering every page.</summary>
    public static BitmapSource FromPdf(byte[] bytes, int pageIndex, int decodeWidth)
    {
        using var bitmap = PDFtoImage.Conversion.ToImage(
            bytes,
            pageIndex,
            options: new PDFtoImage.RenderOptions(Dpi: PdfDpi, BackgroundColor: SkiaSharp.SKColors.White));
        using var stream = new MemoryStream();
        bitmap.Encode(stream, SkiaSharp.SKEncodedImageFormat.Png, 100);
        stream.Position = 0;
        return Decode(stream, decodeWidth);
    }

    /// <summary>Reads an image, applies its EXIF orientation and downsamples it to a thumbnail.</summary>
    public static BitmapSource FromImage(string path, int decodeWidth)
    {
        using var image = new MagickImage(path);
        image.AutoOrient();
        var longSide = Math.Max((int)image.Width, (int)image.Height);
        var target = Math.Max(1, decodeWidth * 2);
        if (longSide > target)
        {
            var scale = (double)target / longSide;
            image.Resize(new MagickGeometry(
                (uint)Math.Max(1, (int)(image.Width * scale)),
                (uint)Math.Max(1, (int)(image.Height * scale))));
        }
        using var stream = new MemoryStream(image.ToByteArray(MagickFormat.Png));
        return Decode(stream, decodeWidth);
    }

    private static BitmapSource Decode(Stream stream, int decodeWidth)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.DecodePixelWidth = Math.Max(1, decodeWidth);
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>Shared chrome pieces for the PDF tool views, built from the theme brushes.</summary>
internal static class PdfToolChrome
{
    /// <summary>A small round icon button; disabled ones stay visible but never click.</summary>
    public static Border IconButton(string glyph, string tooltip, Action click, bool enabled = true)
    {
        var button = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(7),
            Background = Theme.Track,
            Margin = new Thickness(0, 0, 4, 0),
            Opacity = enabled ? 1 : 0.35,
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            ToolTip = tooltip,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = Theme.Icons,
                FontSize = 11,
                Foreground = Theme.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (enabled)
        {
            button.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                click();
            };
        }
        return button;
    }

    /// <summary>A fixed page/image tile: the image and a muted fallback share the same box.</summary>
    public static (Border Box, WpfImage Image, TextBlock Fallback) Thumbnail(double width, double height)
    {
        var image = new WpfImage
        {
            Stretch = Stretch.Uniform,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var fallback = new TextBlock
        {
            Text = "\u2026",
            FontSize = 12,
            Foreground = Theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var box = new Border
        {
            Width = width,
            Height = height,
            Background = Theme.Page,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = new Grid { Children = { image, fallback } },
        };
        return (box, image, fallback);
    }

    /// <summary>The muted "preview unavailable" text shown when a thumbnail cannot be decoded.</summary>
    public static TextBlock Failed(string key) => new()
    {
        Text = T(key),
        FontSize = 10,
        Foreground = Theme.Muted,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = System.Windows.TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
