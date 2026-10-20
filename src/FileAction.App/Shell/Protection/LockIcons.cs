using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.Shell;
using FileAction.App.UI;
using FileAction.Core.Lock;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The shipped Lock badge artwork. The .ico assets are generated deterministically by
/// <c>tools/lock-icons/generate.ps1</c> (16/24/32/48/64/256 PNG frames) and embedded in the app, so a
/// build can never show a missing or half-written icon. The padlock is the owner mockup's two-tone
/// purple lock in the bottom-left corner; document/image/video/archive/folder variants keep the base
/// type recognisable.
/// </summary>
internal static class LockIcons
{
    public const string FileAsset = "locked-document";
    public const string FolderAsset = "locked-folder";
    public const string BadgeAsset = "locked-badge";

    private static readonly string[] Names =
    [
        "locked-document", "locked-image", "locked-video", "locked-archive", "locked-other",
        "locked-folder", "locked-badge",
    ];

    private static readonly int[] Frames = [16, 24, 32, 48, 64, 256];

    public static IReadOnlyList<string> AssetNames => Names;

    public static IReadOnlyList<int> FrameSizes => Frames;

    /// <summary>The raw embedded .ico, for the HKCU registration and for tests.</summary>
    public static byte[] AssetBytes(string name)
    {
        var resource = "FileAction.App.Lock." + name + ".ico";
        using var stream = typeof(LockIcons).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            throw new InvalidOperationException("Missing embedded lock icon: " + resource);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>The type-specific badge for a path: image/video/archive/document or the neutral page.</summary>
    public static string AssetForPath(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff" or ".heic"
                or ".svg" => "locked-image",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" or ".webm" or ".m4v" => "locked-video",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" => "locked-archive",
            ".pdf" or ".doc" or ".docx" or ".txt" or ".rtf" or ".odt" or ".xls" or ".xlsx" or ".ppt"
                or ".pptx" or ".md" or ".csv" => "locked-document",
            _ => "locked-other",
        };
    }

    /// <summary>Writes the .dwenc DefaultIcon. One extension can only have one icon.</summary>
    public static void WriteFileIcon(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, AssetBytes(FileAsset));
    }

    /// <summary>Writes the folder icon used by desktop.ini (normal yellow folder + purple lock).</summary>
    public static void WriteFolderIcon(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, AssetBytes(FolderAsset));
    }

    /// <summary>Decodes one exact-size PNG frame from a shipped .ico (never downscales a larger frame).</summary>
    public static ImageSource? Preview(string asset, int size)
    {
        var png = ReadFrame(AssetBytes(asset), size);
        if (png is null)
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>The single lock-only badge, used by the floating panel and status rows.</summary>
    public static ImageSource? LockGlyph() => Preview(BadgeAsset, 32);

    /// <summary>
    /// Draws the original file's shell icon with the purple lock in the bottom-left corner. This is the
    /// per-file presentation: Windows can only assign one DefaultIcon to the .dwenc extension, so the
    /// original type is shown here (and in the lock window) rather than by a shell extension.
    /// </summary>
    public static ImageSource? ComposeFileBadge(string path, int size = 48)
    {
        var original = FileIcons.Get(path, large: true);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (original is not null)
            {
                dc.DrawImage(original, new Rect(0, 0, size, size));
            }
            else
            {
                var fallback = Preview(AssetForPath(path), size);
                if (fallback is not null)
                    dc.DrawImage(fallback, new Rect(0, 0, size, size));
            }
            DrawBadge(dc, size);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Folder preview uses the shipped folder badge; a file composes its original type icon.</summary>
    public static ImageSource? PreviewForPath(string path, int size = 48)
    {
        if (Directory.Exists(path) || LockFormat.IsProtectedFolderName(path))
            return Preview(FolderAsset, size);
        if (path.EndsWith(LockFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            return Preview(FileAsset, size);
        return ComposeFileBadge(path, size);
    }

    /// <summary>Only used by tests: true when the ICO contains a valid PNG frame at every declared size.</summary>
    public static bool HasAllFrames(byte[] ico)
    {
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1)
            return false;
        var count = BitConverter.ToUInt16(ico, 4);
        if (count != Frames.Length)
            return false;
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var width = ico[entry] == 0 ? 256 : ico[entry];
            var height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            var length = (int)BitConverter.ToUInt32(ico, entry + 8);
            var offset = (int)BitConverter.ToUInt32(ico, entry + 12);
            if (width != Frames[i] || height != Frames[i] || !IsPng(ico, offset, length, Frames[i]))
                return false;
        }
        return true;
    }

    public static byte[]? ReadFrame(byte[] ico, int size)
    {
        if (ico.Length < 6)
            return null;
        var count = BitConverter.ToUInt16(ico, 4);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var width = ico[entry] == 0 ? 256 : ico[entry];
            if (width != size)
                continue;
            var length = (int)BitConverter.ToUInt32(ico, entry + 8);
            var offset = (int)BitConverter.ToUInt32(ico, entry + 12);
            if (offset < 0 || length <= 0 || offset + length > ico.Length)
                return null;
            return ico.AsSpan(offset, length).ToArray();
        }
        return null;
    }

    private static bool IsPng(byte[] data, int offset, int length, int expected)
    {
        if (length < 33 || offset < 0 || offset + length > data.Length)
            return false;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!data.AsSpan(offset, 8).SequenceEqual(signature))
            return false;
        var width = (data[offset + 16] << 24) | (data[offset + 17] << 16) | (data[offset + 18] << 8) | data[offset + 19];
        var height = (data[offset + 20] << 24) | (data[offset + 21] << 16) | (data[offset + 22] << 8) | data[offset + 23];
        return width == expected && height == expected;
    }

    /// <summary>The same two-tone lock as the shipped icon, drawn as vectors for the per-file compose.</summary>
    private static void DrawBadge(DrawingContext dc, int size)
    {
        var u = size / 100.0;
        var body = new Rect(-1 * u, 62 * u, 32 * u, 34 * u);
        var archWidth = 23 * u;
        var archHeight = 22 * u;
        var archX = body.X + ((body.Width - archWidth) / 2);
        var archY = body.Y - (archHeight * 0.55);
        var archBrush = new LinearGradientBrush(
            Color.FromRgb(0xB4, 0x18, 0xE8), Color.FromRgb(0x6A, 0x0D, 0xA8), 0);
        var archPen = new Pen(archBrush, Math.Max(1.5, 7.0 * u))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        var arch = new StreamGeometry();
        using (var ctx = arch.Open())
        {
            ctx.BeginFigure(new Point(archX, archY + (archHeight / 2)), false, false);
            ctx.ArcTo(new Point(archX + archWidth, archY + (archHeight / 2)),
                new Size(archWidth / 2, archHeight / 2), 0, false, SweepDirection.Clockwise, true, false);
        }
        arch.Freeze();
        dc.DrawGeometry(null, archPen, arch);

        var bodyGeometry = new RectangleGeometry(body, 5 * u, 5 * u);
        dc.PushClip(bodyGeometry);
        dc.DrawRectangle(new LinearGradientBrush(
            Color.FromRgb(0xA0, 0x12, 0xD8), Color.FromRgb(0xB4, 0x18, 0xE8), 90), null,
            new Rect(body.X, body.Y, body.Width / 2, body.Height));
        dc.DrawRectangle(new LinearGradientBrush(
            Color.FromRgb(0x6A, 0x0D, 0xA8), Color.FromRgb(0x80, 0x10, 0xC0), 90), null,
            new Rect(body.X + (body.Width / 2), body.Y, body.Width / 2, body.Height));
        dc.Pop();
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x42, 0x08, 0x6C)),
            Math.Max(0.8, 1.0 * u)), body, 5 * u, 5 * u);

        var keyRadius = 3.4 * u;
        var keyX = body.X + (body.Width / 2);
        var keyY = body.Y + (body.Height * 0.30);
        dc.DrawEllipse(Brushes.White, null, new Point(keyX, keyY + keyRadius), keyRadius, keyRadius);
        dc.DrawRectangle(Brushes.White, null, new Rect(
            keyX - (keyRadius * 0.30), keyY + (keyRadius * 1.45), keyRadius * 0.60, keyRadius * 1.55));
    }
}
