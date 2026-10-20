using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// The Lock badge. Only the purple padlock is shipped (16/24/32/48/64/256 PNG frames in
/// <c>padlock.ico</c>); the displayed icon is always composed at lock time from the system's own
/// Explorer icon for that file type or folder, with the padlock on its bottom-left corner.
/// </summary>
internal static class LockIcons
{
    public const string PadlockAsset = "padlock";

    private static readonly int[] Frames = [16, 24, 32, 48, 64, 256];

    public static IReadOnlyList<int> FrameSizes => Frames;

    public static byte[] PadlockBytes => AssetBytes(PadlockAsset);

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

    public static ImageSource? Padlock(int size) => Decode(ReadFrame(PadlockBytes, size));

    public static ImageSource? LockGlyph() => Padlock(32);

    /// <summary>The real shell icon for the item at this exact size (no Lock badge yet).</summary>
    public static ImageSource? SystemIconFor(string path, bool folder, int size) =>
        ShellIcon.Get(path, size, folder);

    /// <summary>System icon + the purple padlock in the bottom-left corner, rendered at exactly this size.</summary>
    public static BitmapSource? Compose(string path, bool folder, int size)
    {
        try
        {
            return ComposeCore(path, folder, size);
        }
        catch (Exception)
        {
            // Rendering the badge must never take down the Lock window or the app self-test.
            return Padlock(size) as BitmapSource;
        }
    }

    private static BitmapSource? ComposeCore(string path, bool folder, int size)
    {
        var baseIcon = ShellIcon.Get(path, size, folder);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (baseIcon is not null)
                dc.DrawImage(baseIcon, new Rect(0, 0, size, size));
            DrawBadge(dc, size);
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>All six sizes in an ICO container, each composed from the system icon at that size.</summary>
    public static byte[] ComposeIco(string path, bool folder)
    {
        var pngs = new List<(int Size, byte[] Png)>();
        foreach (var size in Frames)
        {
            var image = Compose(path, folder, size);
            if (image is null)
                continue;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            pngs.Add((size, stream.ToArray()));
        }
        return IcoBytes(pngs);
    }

    /// <summary>Used by the lock window: system icon + badge for a file, or the composed folder icon.</summary>
    public static ImageSource? PreviewForPath(string path, int size = 48)
    {
        try
        {
            // A .dwenc/.dwdir already has the composed association icon; do not stack a second padlock.
            if (path.EndsWith(FileAction.Core.Lock.LockFormat.FileExtension, StringComparison.OrdinalIgnoreCase) ||
                FileAction.Core.Lock.LockFormat.IsProtectedFolderName(path))
                return Padlock(size);
            return Compose(path, Directory.Exists(path), size);
        }
        catch (Exception)
        {
            return Padlock(size);
        }
    }

    /// <summary>Tests: the ICO must contain a valid PNG frame at every declared size.</summary>
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
            var height = ico[entry + 1] == 0 ? 256 : ico[entry + 1];
            var length = (int)BitConverter.ToUInt32(ico, entry + 8);
            var offset = (int)BitConverter.ToUInt32(ico, entry + 12);
            if (width != size || height != size)
                continue;
            if (offset < 0 || length <= 0 || offset + length > ico.Length)
                return null;
            return ico.AsSpan(offset, length).ToArray();
        }
        return null;
    }

    private static ImageSource? Decode(byte[]? png)
    {
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

    private static byte[] IcoBytes(List<(int Size, byte[] Png)> frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);
        var offset = 6 + (16 * frames.Count);
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames)
            writer.Write(png, 0, png.Length);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>The mockup padlock: #8224D4 body, #B48AE0 highlight, white keyhole, bottom-left, ~43%.</summary>
    private static void DrawBadge(DrawingContext dc, int size)
    {
        var u = size / 100.0;
        var body = new Rect(-2 * u, 64 * u, 32 * u, 32 * u);
        var archWidth = 22 * u;
        var archHeight = 20 * u;
        var archX = body.X + ((body.Width - archWidth) / 2);
        var archY = body.Y - (archHeight * 0.55);
        var archPen = new Pen(new SolidColorBrush(Color.FromRgb(0x82, 0x24, 0xD4)), Math.Max(1.5, 7.0 * u))
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

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x82, 0x24, 0xD4)), null, body, 5 * u, 5 * u);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xB4, 0x8A, 0xE0)), null,
            new Rect(body.X + (2 * u), body.Y + (2 * u), body.Width - (4 * u), 7 * u), 3.5 * u, 3.5 * u);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x0B, 0x8E)),
            Math.Max(1.0, 1.2 * u)), body, 5 * u, 5 * u);

        var keyRadius = 3.2 * u;
        var keyX = body.X + (body.Width / 2);
        var keyY = body.Y + (body.Height * 0.42);
        dc.DrawEllipse(Brushes.White, null, new Point(keyX, keyY), keyRadius, keyRadius);
        dc.DrawRectangle(Brushes.White, null, new Rect(
            keyX - (keyRadius * 0.32), keyY + (keyRadius * 0.9), keyRadius * 0.64, keyRadius * 1.8));
    }
}
