namespace FileAction.Core;

/// <summary>How the images share the canvas.</summary>
public enum CollageLayout
{
    /// <summary>Near-square cells; the last row is centred when the count is not a multiple of the column count.</summary>
    Grid,

    /// <summary>One horizontal band; every image is a column of equal width.</summary>
    Row,

    /// <summary>One vertical band; every image is a row of equal height.</summary>
    Column,

    /// <summary>The first image takes most of the canvas, the others stack beside it.</summary>
    Featured,
}

/// <summary>How an image fills its cell.</summary>
public enum CollageFit
{
    /// <summary>Cover: scale until both edges cover the cell, crop the overflow.</summary>
    Fill,

    /// <summary>Contain: scale until the whole image fits, leaving background around it.</summary>
    Fit,
}

/// <summary>The canvas background.</summary>
public enum CollageBackgroundKind
{
    /// <summary>One flat colour.</summary>
    Solid,

    /// <summary>A vertical two-colour gradient (top to bottom).</summary>
    Gradient,
}

/// <summary>A rectangle in canvas or source pixels, with a top-left origin.</summary>
public readonly record struct CollageRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double Area => Width * Height;
}

/// <summary>One image's slot on the canvas.</summary>
public readonly record struct CollageCell(int Index, CollageRect Bounds);

/// <summary>
/// Where one image is drawn: the cell it belongs to, the destination rectangle on the canvas, and the
/// part of the original image that is used (the full image for <see cref="CollageFit.Fit"/>, a centred
/// crop for <see cref="CollageFit.Fill"/>).
/// </summary>
public readonly record struct CollagePlacement(int Index, CollageRect Cell, CollageRect Image, CollageRect Source);

/// <summary>An opaque 8-bit RGB colour, so the core stays free of WPF and Magick types.</summary>
public readonly record struct CollageColor(byte R, byte G, byte B)
{
    public static readonly CollageColor Black = new(0, 0, 0);
    public static readonly CollageColor White = new(255, 255, 255);

    /// <summary>Parses <c>#RGB</c> or <c>#RRGGBB</c> (the <c>#</c> is optional).</summary>
    public static CollageColor FromHex(string value)
    {
        if (!TryFromHex(value, out var color))
            throw new FormatException($"'{value}' is not an RGB colour such as #14121E.");
        return color;
    }

    public static bool TryFromHex(string? value, out CollageColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = value.Trim().TrimStart('#');
        if (text.Length == 3)
            text = string.Concat(text.Select(c => new string(c, 2)));
        if (text.Length != 6 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var packed))
            return false;
        color = new CollageColor((byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
        return true;
    }

    /// <summary><c>#RRGGBB</c>, the shape the string tables and the renderer use.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Linear blend towards <paramref name="other"/>; 0 keeps this colour, 1 returns the other.</summary>
    public CollageColor Mix(CollageColor other, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return new CollageColor(
            (byte)Math.Round(R + (other.R - R) * t),
            (byte)Math.Round(G + (other.G - G) * t),
            (byte)Math.Round(B + (other.B - B) * t));
    }

    public CollageColor Lighten(double amount) => Mix(White, amount);

    public CollageColor Darken(double amount) => Mix(Black, amount);
}

/// <summary>The canvas fill: one colour, or a vertical two-colour gradient.</summary>
public sealed record CollageBackground(CollageBackgroundKind Kind, CollageColor Start, CollageColor End)
{
    public static CollageBackground Solid(CollageColor color) => new(CollageBackgroundKind.Solid, color, color);

    public static CollageBackground Gradient(CollageColor start, CollageColor end) =>
        new(CollageBackgroundKind.Gradient, start, end);

    /// <summary>
    /// A background for one palette colour: the same colour for solid, the colour into its lighter shade
    /// for a gradient. This is what the tool's colour swatches use.
    /// </summary>
    public static CollageBackground For(CollageColor color, CollageBackgroundKind kind) =>
        kind == CollageBackgroundKind.Gradient ? Gradient(color, color.Lighten(0.42)) : Solid(color);

    public static CollageBackground Default { get; } = Solid(CollageColor.FromHex("#14121E"));
}

/// <summary>The background colours the tool offers, dark first so the default matches the app.</summary>
public static class CollagePalette
{
    public static IReadOnlyList<CollageColor> Colors { get; } =
    [
        CollageColor.FromHex("#14121E"),
        CollageColor.FromHex("#FFFFFF"),
        CollageColor.FromHex("#8224D4"),
        CollageColor.FromHex("#F3D9E6"),
        CollageColor.FromHex("#BFD8F2"),
    ];
}

/// <summary>An output canvas size in pixels.</summary>
public readonly record struct CollageSize(int Width, int Height)
{
    public double Aspect => Width / (double)Math.Max(1, Height);
}

/// <summary>The output sizes the tool offers, plus the bounds a typed width/height is clamped to.</summary>
public static class CollagePresets
{
    public const int MinSize = 64;
    public const int MaxSize = 8192;

    public static readonly CollageSize Square = new(1080, 1080);
    public static readonly CollageSize Landscape = new(1920, 1080);
    public static readonly CollageSize Portrait = new(1080, 1920);

    public static IReadOnlyList<CollageSize> All { get; } = [Square, Landscape, Portrait];

    public static CollageSize Clamp(int width, int height) =>
        new(Math.Clamp(width, MinSize, MaxSize), Math.Clamp(height, MinSize, MaxSize));
}

/// <summary>Everything the renderer needs to draw one collage.</summary>
public sealed record CollageOptions
{
    public CollageLayout Layout { get; init; } = CollageLayout.Grid;

    public CollageFit Fit { get; init; } = CollageFit.Fill;

    public CollageSize Size { get; init; } = CollagePresets.Square;

    /// <summary>Gap between neighbouring cells, in output pixels.</summary>
    public double Spacing { get; init; } = 12;

    /// <summary>Corner radius of every cell, in output pixels (clamped per cell).</summary>
    public double Radius { get; init; } = 16;

    public CollageBackground Background { get; init; } = CollageBackground.Default;

    public static CollageOptions Default { get; } = new();
}

/// <summary>
/// The layout rules for a collage: how many cells each layout makes, where they sit (spacing and corner
/// radius included), and which part of each image fills its cell. Pure functions with no WPF, Magick or
/// file-system dependency, so every case below is unit tested without a window or a bitmap.
/// </summary>
public static class CollageMath
{
    /// <summary>Share of the width the first image keeps in <see cref="CollageLayout.Featured"/>.</summary>
    public const double FeaturedShare = 0.65;

    /// <summary>
    /// The cell rectangles in reading order. <paramref name="spacing"/> is the gap between neighbours;
    /// it is capped so cells never collapse to zero. A partial last grid row is centred and keeps the
    /// same cell size as the full rows.
    /// </summary>
    public static IReadOnlyList<CollageCell> Cells(
        int count, CollageLayout layout, double width, double height, double spacing)
    {
        if (count <= 0)
            return [];
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        return layout switch
        {
            CollageLayout.Row => Row(count, width, height, spacing),
            CollageLayout.Column => Column(count, width, height, spacing),
            CollageLayout.Featured => Featured(count, width, height, spacing),
            _ => Grid(count, width, height, spacing),
        };
    }

    /// <summary>Placements for <paramref name="images"/> (one per cell, in the same order).</summary>
    public static IReadOnlyList<CollagePlacement> Placements(
        IReadOnlyList<CollageCell> cells, IReadOnlyList<CollageSize> images, CollageFit fit)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(images);
        var count = Math.Min(cells.Count, images.Count);
        var placements = new List<CollagePlacement>(count);
        for (var i = 0; i < count; i++)
        {
            var image = images[i];
            placements.Add(Place(cells[i], image.Width, image.Height, fit));
        }
        return placements;
    }

    /// <summary>
    /// Where one image is drawn inside its cell. <see cref="CollageFit.Fill"/> covers the cell and
    /// reports the centred source crop; <see cref="CollageFit.Fit"/> reports the contained destination
    /// rectangle and keeps the whole source.
    /// </summary>
    public static CollagePlacement Place(CollageCell cell, int imageWidth, int imageHeight, CollageFit fit)
    {
        var width = Math.Max(1, imageWidth);
        var height = Math.Max(1, imageHeight);
        var bounds = Normalized(cell.Bounds);

        if (fit == CollageFit.Fill)
        {
            var target = bounds.Width / Math.Max(double.Epsilon, bounds.Height);
            double sourceWidth, sourceHeight;
            if (width / (double)height > target)
            {
                sourceHeight = height;
                sourceWidth = height * target;
            }
            else
            {
                sourceWidth = width;
                sourceHeight = width / target;
            }

            return new CollagePlacement(
                cell.Index,
                bounds,
                bounds,
                new CollageRect((width - sourceWidth) / 2, (height - sourceHeight) / 2, sourceWidth, sourceHeight));
        }

        var scale = Math.Min(bounds.Width / width, bounds.Height / height);
        var drawnWidth = width * scale;
        var drawnHeight = height * scale;
        return new CollagePlacement(
            cell.Index,
            bounds,
            new CollageRect(
                bounds.X + (bounds.Width - drawnWidth) / 2,
                bounds.Y + (bounds.Height - drawnHeight) / 2,
                drawnWidth,
                drawnHeight),
            new CollageRect(0, 0, width, height));
    }

    /// <summary>The corner radius a cell can actually show: never more than half its shorter side.</summary>
    public static double CornerRadius(CollageRect cell, double requested) =>
        Math.Clamp(requested, 0, Math.Max(0, Math.Min(cell.Width, cell.Height) / 2));

    /// <summary>The spacing a layout can honour without making a cell zero-sized.</summary>
    public static double ClampSpacing(int count, CollageLayout layout, double width, double height, double spacing)
    {
        if (count <= 1 || spacing <= 0)
            return 0;
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var horizontalGaps = layout switch
        {
            CollageLayout.Column => 0,
            CollageLayout.Row => count - 1,
            CollageLayout.Grid => GridColumns(count) - 1,
            _ => 1,
        };
        var verticalGaps = layout switch
        {
            CollageLayout.Row => 0,
            CollageLayout.Column => count - 1,
            CollageLayout.Grid => GridRows(count) - 1,
            _ => Math.Max(0, count - 2),
        };
        var cap = double.MaxValue;
        if (horizontalGaps > 0)
            cap = Math.Min(cap, (width - 1) / horizontalGaps);
        if (verticalGaps > 0)
            cap = Math.Min(cap, (height - 1) / verticalGaps);
        return Math.Clamp(spacing, 0, Math.Max(0, cap));
    }

    private static IReadOnlyList<CollageCell> Grid(int count, double width, double height, double spacing)
    {
        var columns = GridColumns(count);
        var rows = GridRows(count);
        var gap = ClampSpacing(count, CollageLayout.Grid, width, height, spacing);
        var cellWidth = (width - gap * (columns - 1)) / columns;
        var cellHeight = (height - gap * (rows - 1)) / rows;
        var cells = new List<CollageCell>(count);
        for (var i = 0; i < count; i++)
        {
            var row = i / columns;
            var column = i % columns;
            var inRow = Math.Min(columns, count - row * columns);
            var rowWidth = inRow * cellWidth + (inRow - 1) * gap;
            var left = Math.Max(0, (width - rowWidth) / 2) + column * (cellWidth + gap);
            var top = row * (cellHeight + gap);
            cells.Add(new CollageCell(i, new CollageRect(left, top, cellWidth, cellHeight)));
        }
        return cells;
    }

    private static IReadOnlyList<CollageCell> Row(int count, double width, double height, double spacing)
    {
        var gap = ClampSpacing(count, CollageLayout.Row, width, height, spacing);
        var cellWidth = (width - gap * (count - 1)) / count;
        var cells = new List<CollageCell>(count);
        for (var i = 0; i < count; i++)
            cells.Add(new CollageCell(i, new CollageRect(i * (cellWidth + gap), 0, cellWidth, height)));
        return cells;
    }

    private static IReadOnlyList<CollageCell> Column(int count, double width, double height, double spacing)
    {
        var gap = ClampSpacing(count, CollageLayout.Column, width, height, spacing);
        var cellHeight = (height - gap * (count - 1)) / count;
        var cells = new List<CollageCell>(count);
        for (var i = 0; i < count; i++)
            cells.Add(new CollageCell(i, new CollageRect(0, i * (cellHeight + gap), width, cellHeight)));
        return cells;
    }

    private static IReadOnlyList<CollageCell> Featured(int count, double width, double height, double spacing)
    {
        var gap = ClampSpacing(count, CollageLayout.Featured, width, height, spacing);
        if (count == 1)
            return [new CollageCell(0, new CollageRect(0, 0, width, height))];

        var available = Math.Max(0, width - gap);
        var featuredWidth = available * FeaturedShare;
        var restWidth = available - featuredWidth;
        var restHeight = (height - gap * (count - 2)) / (count - 1);
        var cells = new List<CollageCell>(count)
        {
            new(0, new CollageRect(0, 0, featuredWidth, height)),
        };
        for (var i = 1; i < count; i++)
            cells.Add(new CollageCell(i, new CollageRect(featuredWidth + gap, (i - 1) * (restHeight + gap), restWidth, restHeight)));
        return cells;
    }

    private static int GridColumns(int count) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));

    private static int GridRows(int count) => Math.Max(1, (int)Math.Ceiling(count / (double)GridColumns(count)));

    private static CollageRect Normalized(CollageRect rect) =>
        new(rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
}

/// <summary>
/// The drag-reorder model for the filmstrip: a permutation of the input indices. Position 0 is drawn
/// first (grid reading order, or the featured slot); <see cref="Move"/> takes a position, not the
/// original index, which is what a drag reports.
/// </summary>
public sealed class CollageOrder
{
    private readonly List<int> _source;

    public CollageOrder(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _source = Enumerable.Range(0, count).ToList();
    }

    private CollageOrder(IEnumerable<int> source) => _source = [.. source];

    public int Count => _source.Count;

    /// <summary>The original input index for each strip position.</summary>
    public IReadOnlyList<int> SourceIndices => _source;

    public int this[int position] => _source[position];

    /// <summary>Moves the item at strip position <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Count || to >= Count)
            return false;
        var item = _source[from];
        _source.RemoveAt(from);
        _source.Insert(to, item);
        return true;
    }

    /// <summary>Moves one position left (-1) or right (+1), stopping at the ends.</summary>
    public bool MoveBy(int position, int delta) =>
        Move(position, Math.Clamp(position + delta, 0, Count - 1));

    public bool Swap(int first, int second)
    {
        if (first == second || first < 0 || second < 0 || first >= Count || second >= Count)
            return false;
        (_source[first], _source[second]) = (_source[second], _source[first]);
        return true;
    }

    /// <summary>Back to the order the files were dropped in.</summary>
    public void Reset() => _source.Sort();

    public int PositionOf(int sourceIndex) => _source.IndexOf(sourceIndex);

    public CollageOrder Clone() => new(_source);

    /// <summary>Applies the order to a list that has exactly one item per position.</summary>
    public IReadOnlyList<T> Apply<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count != Count)
            throw new ArgumentException($"The order has {Count} positions but the list has {items.Count} items.", nameof(items));
        return _source.Select(index => items[index]).ToList();
    }

    /// <summary>Rebuilds an order from a permutation, rejecting duplicates and out-of-range indices.</summary>
    public static CollageOrder FromSourceOrder(IEnumerable<int> sourceIndices)
    {
        ArgumentNullException.ThrowIfNull(sourceIndices);
        var list = sourceIndices.ToList();
        if (list.Count == 0)
            return new CollageOrder(0);
        var seen = new HashSet<int>();
        foreach (var index in list)
        {
            if (index < 0 || index >= list.Count || !seen.Add(index))
                throw new ArgumentException("The source order must be a permutation of its own indices.", nameof(sourceIndices));
        }
        return new CollageOrder(list);
    }
}
