using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.UI;

namespace FileAction.App.Shell.Protection;

/// <summary>
/// Draws the pink-lock badges and writes real multi-size <c>.ico</c> files (16/24/32/48/256) without any
/// image library: the pixels are composed in code and encoded as PNG frames inside an ICO container, which
/// Windows Vista and later read in Explorer, the taskbar and dialogs.
/// </summary>
internal static class LockIcons
{
    public static readonly IReadOnlyList<int> Sizes = [16, 24, 32, 48, 256];

    /// <summary>The icon for <c>.dwenc</c>: a document with the pink lock badge.</summary>
    public static byte[] BuildFileIcon() => BuildIco(folder: false);

    /// <summary>The icon for <c>.dwdir</c>: a folder with the pink lock badge.</summary>
    public static byte[] BuildFolderIcon() => BuildIco(folder: true);

    public static void WriteFileIcon(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllBytes(path, BuildFileIcon());
    }

    public static void WriteFolderIcon(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllBytes(path, BuildFolderIcon());
    }

    /// <summary>One frame as PNG; used by tests and by the small in-app badge.</summary>
    public static byte[] BuildPng(bool folder, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
        var scale = 4;
        var raster = new Raster(size * scale, size * scale);
        if (folder)
            DrawFolder(raster);
        else
            DrawDocument(raster);
        DrawLock(raster);
        var small = raster.Downsample(scale);
        return Png.Encode(small.Width, small.Height, small.Pixels);
    }

    private static byte[] BuildIco(bool folder)
    {
        var frames = Sizes.Select(size => (Size: size, Png: BuildPng(folder, size))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)frames.Count);
        var offset = 6 + (16 * frames.Count);
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); // palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames)
            writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    // ------------------------------------------------------------------ drawing

    private static readonly (byte R, byte G, byte B) Paper = (0xFD, 0xFB, 0xFF);
    private static readonly (byte R, byte G, byte B) PaperEdge = (0xC9, 0xBE, 0xDB);
    private static readonly (byte R, byte G, byte B) FolderFill = (0xB4, 0x8A, 0xE0);
    private static readonly (byte R, byte G, byte B) FolderBack = (0x8C, 0x5A, 0xC8);

    private static (byte R, byte G, byte B) Badge =>
        ((byte)Theme.ProtectBadgeColor.R, (byte)Theme.ProtectBadgeColor.G, (byte)Theme.ProtectBadgeColor.B);

    private static void DrawDocument(Raster canvas)
    {
        var s = canvas.Width;
        var left = 0.20 * s;
        var top = 0.07 * s;
        var width = 0.60 * s;
        var height = 0.74 * s;
        var radius = 0.07 * s;
        canvas.FillRoundedRect(left, top, width, height, radius, Paper);
        canvas.StrokeRoundedRect(left, top, width, height, radius, PaperEdge, Math.Max(1, 0.018 * s));
        // A folded corner and three text lines make the frame readable at 16 px.
        canvas.FillTriangle(left + width * 0.62, top, left + width, top, left + width, top + height * 0.25, PaperEdge);
        var line = Math.Max(1, 0.045 * s);
        for (var i = 0; i < 3; i++)
        {
            canvas.FillRoundedRect(left + width * 0.16, top + height * (0.34 + (i * 0.16)),
                width * (i == 2 ? 0.42 : 0.62), line, line / 2, PaperEdge);
        }
        DrawBadgeBacking(canvas);
    }

    private static void DrawFolder(Raster canvas)
    {
        var s = canvas.Width;
        var left = 0.10 * s;
        var top = 0.18 * s;
        var width = 0.72 * s;
        var height = 0.56 * s;
        // Back tab, then the front body: two rounded rectangles read as a folder at every size.
        canvas.FillRoundedRect(left + width * 0.06, top - height * 0.16, width * 0.42, height * 0.34,
            height * 0.10, FolderBack);
        canvas.FillRoundedRect(left, top, width, height, height * 0.14, FolderFill);
        canvas.StrokeRoundedRect(left, top, width, height, height * 0.14, FolderBack, Math.Max(1, 0.016 * s));
        DrawBadgeBacking(canvas);
    }

    private static void DrawBadgeBacking(Raster canvas)
    {
        var s = canvas.Width;
        canvas.FillCircle(0.76 * s, 0.77 * s, 0.235 * s, (0xFF, 0xFF, 0xFF));
    }

    private static void DrawLock(Raster canvas)
    {
        var s = canvas.Width;
        var badge = Badge;
        var cx = 0.755 * s;
        var cy = 0.83 * s;
        var bodyWidth = 0.30 * s;
        var bodyHeight = 0.22 * s;
        var bodyLeft = cx - (bodyWidth / 2);
        var bodyTop = cy - (bodyHeight / 2);

        // Shackle: an upper-half ring centred on the body.
        var shackleRadius = 0.085 * s;
        var shackleThickness = Math.Max(1.2, 0.035 * s);
        canvas.FillUpperRing(cx, bodyTop + 0.01 * s, shackleRadius, shackleRadius - shackleThickness, badge);
        canvas.FillRoundedRect(bodyLeft, bodyTop, bodyWidth, bodyHeight, 0.045 * s, badge);
        canvas.FillCircle(cx, bodyTop + bodyHeight * 0.42, 0.030 * s, (0xFF, 0xFF, 0xFF));
        canvas.FillRoundedRect(cx - (0.012 * s), bodyTop + bodyHeight * 0.42, 0.024 * s, 0.075 * s,
            0.012 * s, (0xFF, 0xFF, 0xFF));
    }

    // ------------------------------------------------------------------ raster

    private sealed class Raster(int width, int height)
    {
        public int Width { get; } = width;
        public int Height { get; } = height;
        public byte[] Pixels { get; } = new byte[width * height * 4];

        public Raster Downsample(int factor)
        {
            var target = new Raster(Width / factor, Height / factor);
            for (var y = 0; y < target.Height; y++)
            {
                for (var x = 0; x < target.Width; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (var sy = 0; sy < factor; sy++)
                    {
                        for (var sx = 0; sx < factor; sx++)
                        {
                            var i = (((y * factor) + sy) * Width + ((x * factor) + sx)) * 4;
                            r += Pixels[i];
                            g += Pixels[i + 1];
                            b += Pixels[i + 2];
                            a += Pixels[i + 3];
                        }
                    }
                    var n = factor * factor;
                    var o = ((y * target.Width) + x) * 4;
                    target.Pixels[o] = (byte)(r / n);
                    target.Pixels[o + 1] = (byte)(g / n);
                    target.Pixels[o + 2] = (byte)(b / n);
                    target.Pixels[o + 3] = (byte)(a / n);
                }
            }
            return target;
        }

        public void Blend(int x, int y, (byte R, byte G, byte B) color, double alpha = 1)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0)
                return;
            var i = ((y * Width) + x) * 4;
            var a = Math.Clamp(alpha, 0, 1);
            var existingAlpha = Pixels[i + 3] / 255.0;
            var outAlpha = a + (existingAlpha * (1 - a));
            if (outAlpha <= 0)
                return;
            Pixels[i] = Mix(Pixels[i], color.R, a, existingAlpha, outAlpha);
            Pixels[i + 1] = Mix(Pixels[i + 1], color.G, a, existingAlpha, outAlpha);
            Pixels[i + 2] = Mix(Pixels[i + 2], color.B, a, existingAlpha, outAlpha);
            Pixels[i + 3] = (byte)Math.Round(outAlpha * 255);
        }

        private static byte Mix(byte destination, byte source, double a, double existingAlpha, double outAlpha)
        {
            var value = ((source * a) + (destination * existingAlpha * (1 - a))) / outAlpha;
            return (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        public void FillCircle(double cx, double cy, double radius, (byte R, byte G, byte B) color)
        {
            for (var y = (int)Math.Floor(cy - radius); y <= (int)Math.Ceiling(cy + radius); y++)
            {
                for (var x = (int)Math.Floor(cx - radius); x <= (int)Math.Ceiling(cx + radius); x++)
                {
                    var dx = x + 0.5 - cx;
                    var dy = y + 0.5 - cy;
                    if ((dx * dx) + (dy * dy) <= radius * radius)
                        Blend(x, y, color);
                }
            }
        }

        public void FillUpperRing(double cx, double cy, double outer, double inner,
            (byte R, byte G, byte B) color)
        {
            for (var y = (int)Math.Floor(cy - outer); y <= (int)Math.Ceiling(cy); y++)
            {
                for (var x = (int)Math.Floor(cx - outer); x <= (int)Math.Ceiling(cx + outer); x++)
                {
                    var dx = x + 0.5 - cx;
                    var dy = y + 0.5 - cy;
                    var distance = Math.Sqrt((dx * dx) + (dy * dy));
                    if (distance <= outer && distance >= inner && dy <= 0)
                        Blend(x, y, color);
                }
            }
            // Straight legs down to the body, so the shackle meets it.
            var leg = outer - inner;
            FillRect(cx - outer, cy - 1, leg, outer * 0.55, color);
            FillRect(cx + inner, cy - 1, leg, outer * 0.55, color);
        }

        public void FillRect(double x, double y, double width, double height, (byte R, byte G, byte B) color)
        {
            for (var py = (int)Math.Floor(y); py < (int)Math.Ceiling(y + height); py++)
            {
                for (var px = (int)Math.Floor(x); px < (int)Math.Ceiling(x + width); px++)
                    Blend(px, py, color);
            }
        }

        public void FillRoundedRect(double x, double y, double width, double height, double radius,
            (byte R, byte G, byte B) color)
        {
            var r = Math.Min(radius, Math.Min(width, height) / 2);
            for (var py = (int)Math.Floor(y); py < (int)Math.Ceiling(y + height); py++)
            {
                for (var px = (int)Math.Floor(x); px < (int)Math.Ceiling(x + width); px++)
                {
                    var cx = px + 0.5;
                    var cy = py + 0.5;
                    var dx = Math.Max(Math.Max(x + r - cx, cx - (x + width - r)), 0);
                    var dy = Math.Max(Math.Max(y + r - cy, cy - (y + height - r)), 0);
                    if ((dx * dx) + (dy * dy) <= r * r)
                        Blend(px, py, color);
                }
            }
        }

        public void StrokeRoundedRect(double x, double y, double width, double height, double radius,
            (byte R, byte G, byte B) color, double thickness)
        {
            var half = thickness / 2;
            for (var py = (int)Math.Floor(y - thickness); py < (int)Math.Ceiling(y + height + thickness); py++)
            {
                for (var px = (int)Math.Floor(x - thickness); px < (int)Math.Ceiling(x + width + thickness); px++)
                {
                    var cx = px + 0.5;
                    var cy = py + 0.5;
                    if (!InsideRoundedRect(cx, cy, x - half, y - half, width + thickness, height + thickness, radius))
                        continue;
                    if (InsideRoundedRect(cx, cy, x + half, y + half, width - thickness, height - thickness, radius))
                        continue;
                    Blend(px, py, color);
                }
            }
        }

        public void FillTriangle(double x1, double y1, double x2, double y2, double x3, double y3,
            (byte R, byte G, byte B) color)
        {
            var minX = (int)Math.Floor(Math.Min(x1, Math.Min(x2, x3)));
            var maxX = (int)Math.Ceiling(Math.Max(x1, Math.Max(x2, x3)));
            var minY = (int)Math.Floor(Math.Min(y1, Math.Min(y2, y3)));
            var maxY = (int)Math.Ceiling(Math.Max(y1, Math.Max(y2, y3)));
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var px = x + 0.5;
                    var py = y + 0.5;
                    var d1 = Sign(px, py, x1, y1, x2, y2);
                    var d2 = Sign(px, py, x2, y2, x3, y3);
                    var d3 = Sign(px, py, x3, y3, x1, y1);
                    var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
                    var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(hasNegative && hasPositive))
                        Blend(x, y, color);
                }
            }
        }

        private static double Sign(double px, double py, double ax, double ay, double bx, double by) =>
            ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));

        private static bool InsideRoundedRect(double px, double py, double x, double y, double width,
            double height, double radius)
        {
            if (width <= 0 || height <= 0 || px < x || py < y || px > x + width || py > y + height)
                return false;
            var r = Math.Min(radius, Math.Min(width, height) / 2);
            var dx = Math.Max(Math.Max(x + r - px, px - (x + width - r)), 0);
            var dy = Math.Max(Math.Max(y + r - py, py - (y + height - r)), 0);
            return (dx * dx) + (dy * dy) <= r * r;
        }
    }

    // ------------------------------------------------------------------ PNG

    internal static class Png
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Encode(int width, int height, byte[] rgba)
        {
            using var stream = new MemoryStream();
            stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var header = new byte[13];
            WriteBigEndian(header, 0, (uint)width);
            WriteBigEndian(header, 4, (uint)height);
            header[8] = 8; // bit depth
            header[9] = 6; // RGBA
            header[10] = 0;
            header[11] = 0;
            header[12] = 0;
            WriteChunk(stream, "IHDR", header);

            var raw = new byte[(width * 4 + 1) * height];
            var cursor = 0;
            for (var y = 0; y < height; y++)
            {
                raw[cursor++] = 0; // filter: none
                Buffer.BlockCopy(rgba, y * width * 4, raw, cursor, width * 4);
                cursor += width * 4;
            }

            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);
                compressed.WriteByte(0x9C);
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                    deflate.Write(raw, 0, raw.Length);
                var adler = Adler32(raw);
                compressed.WriteByte((byte)(adler >> 24));
                compressed.WriteByte((byte)(adler >> 16));
                compressed.WriteByte((byte)(adler >> 8));
                compressed.WriteByte((byte)adler);
                WriteChunk(stream, "IDAT", compressed.ToArray());
            }
            WriteChunk(stream, "IEND", []);
            return stream.ToArray();
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            stream.Write(length);
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes);
            stream.Write(data);
            var crcInput = new byte[typeBytes.Length + data.Length];
            typeBytes.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, typeBytes.Length);
            var crc = new byte[4];
            WriteBigEndian(crc, 0, Crc32(crcInput));
            stream.Write(crc);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static uint Crc32(byte[] data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }

    // ------------------------------------------------------------------ WPF badge for the result list

    /// <summary>The lock glyph as a WPF image for the tool's result list (32 px PNG).</summary>
    public static ImageSource LockGlyph()
    {
        var bytes = BuildPng(folder: false, 32);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
