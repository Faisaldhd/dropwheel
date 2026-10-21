using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.Core;
using ImageMagick;
using SkiaSharp;
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
        // WPF's BitmapImage/FinalizeCreation touches ImagingCache, which throws ArgumentNullException("key")
        // when decoding off the UI thread (the PDF tools decode in Task.Run). Skia decodes and scales into a
        // plain BGRA buffer, then BitmapSource.Create makes a frozen frame with no WPF cache involvement.
        using var source = SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException("The PDF page image could not be decoded.");
        var target = Math.Max(1, decodeWidth);
        var height = Math.Max(1, (int)Math.Round(source.Height * (double)target / source.Width));
        using var resized = source.Resize(
            new SKImageInfo(target, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.High) ?? throw new InvalidOperationException("The PDF page image could not be resized.");
        var info = new SKImageInfo(resized.Width, resized.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        Marshal.Copy(resized.GetPixels(), pixels, 0, pixels.Length);
        var bitmap = BitmapSource.Create(info.Width, info.Height, 96, 96,
            PixelFormats.Bgra32, null, pixels, info.RowBytes);
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
            FontSize = FallbackFontSize(width),
            Foreground = Theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
            MaxWidth = FallbackWidth(width),
        };
        var box = new Border
        {
            Width = width,
            Height = height,
            Background = Theme.Page,
            CornerRadius = new CornerRadius(Design.Radius.Control),
            Child = new Grid { Children = { image, fallback } },
        };

        // Border draws its own background rounded but never clips its children: without a rounded
        // geometry the page preview paints square over the tile's corners. Same geometry as
        // MediaSurface.Round, applied now because these tiles have a fixed width and height.
        box.Clip = new RectangleGeometry(
            new Rect(0, 0, width, height), Design.Radius.Control, Design.Radius.Control);
        return (box, image, fallback);
    }

    /// <summary>
    /// Shows the muted failure text in a tile's fallback, wrapped and fitted to the tile width so a long
    /// sentence cannot be clipped in a small 44 x 58 merge row.
    /// </summary>
    public static void Fail(TextBlock fallback, string key, double width)
    {
        var failed = Failed(key);
        fallback.Text = failed.Text;
        fallback.FontSize = FallbackFontSize(width);
        fallback.Foreground = failed.Foreground;
        fallback.TextWrapping = failed.TextWrapping;
        fallback.TextAlignment = failed.TextAlignment;
        fallback.MaxWidth = FallbackWidth(width);
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

    private static double FallbackFontSize(double width) => width < 60 ? 9 : 10;

    private static double FallbackWidth(double width) => Math.Max(1, width - 4);
}
