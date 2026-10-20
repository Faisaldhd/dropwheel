using System.Security.Cryptography;
using System.Windows.Media;
using FileAction.App.UI;
using FileAction.App.UI.Tools.Collage;
using FileAction.Core;
using ImageMagick;

namespace FileAction.Tests;

/// <summary>
/// The pure collage model behind the Collage tool: cell packing for each layout (bounds, spacing, odd
/// counts), cover/contain placement inside a cell, output presets and the filmstrip reorder model.
/// </summary>
public class CollageTests
{
    private const double Precision = 6;

    private static readonly CollageLayout[] Layouts =
        [CollageLayout.Grid, CollageLayout.Row, CollageLayout.Column, CollageLayout.Featured];

    [Fact]
    public void Row_cells_span_the_canvas_left_to_right()
    {
        var cells = CollageMath.Cells(4, CollageLayout.Row, 1000, 500, 10);

        Assert.Equal(4, cells.Count);
        Assert.Equal(0, cells[0].Bounds.X, Precision);
        Assert.Equal(242.5, cells[0].Bounds.Width, Precision);
        Assert.Equal(252.5, cells[1].Bounds.X, Precision);
        Assert.Equal(1000, cells[^1].Bounds.Right, Precision);
        Assert.All(cells, cell => Assert.Equal(500, cell.Bounds.Height, Precision));
    }

    [Fact]
    public void Column_cells_span_the_canvas_top_to_bottom()
    {
        var cells = CollageMath.Cells(3, CollageLayout.Column, 800, 600, 6);

        Assert.Equal(3, cells.Count);
        Assert.Equal(0, cells[0].Bounds.Y, Precision);
        Assert.Equal(196, cells[0].Bounds.Height, Precision);
        Assert.Equal(600, cells[^1].Bounds.Bottom, Precision);
        Assert.All(cells, cell => Assert.Equal(800, cell.Bounds.Width, Precision));
    }

    [Fact]
    public void Grid_uses_near_square_columns_and_centres_a_partial_last_row()
    {
        var cells = CollageMath.Cells(3, CollageLayout.Grid, 1000, 500, 10);

        Assert.Equal(3, cells.Count);
        // Two columns: (1000 - 10) / 2 = 495 wide; two rows: (500 - 10) / 2 = 245 high.
        Assert.Equal(495, cells[0].Bounds.Width, Precision);
        Assert.Equal(245, cells[0].Bounds.Height, Precision);
        Assert.Equal(0, cells[0].Bounds.X, Precision);
        Assert.Equal(505, cells[1].Bounds.X, Precision);
        // The single cell of the last row keeps the cell size and is centred.
        Assert.Equal(252.5, cells[2].Bounds.X, Precision);
        Assert.Equal(495, cells[2].Bounds.Width, Precision);
        Assert.Equal(255, cells[2].Bounds.Y, Precision);
    }

    [Fact]
    public void Grid_of_four_is_two_by_two()
    {
        var cells = CollageMath.Cells(4, CollageLayout.Grid, 900, 900, 0);

        Assert.Equal(450, cells[0].Bounds.Width, Precision);
        Assert.Equal(450, cells[3].Bounds.X, Precision);
        Assert.Equal(450, cells[3].Bounds.Y, Precision);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void Every_layout_places_all_images_inside_the_canvas_without_overlap(int count)
    {
        foreach (var layout in Layouts)
        {
            var cells = CollageMath.Cells(count, layout, 1200, 800, 12);

            Assert.Equal(count, cells.Count);
            Assert.Equal(Enumerable.Range(0, count), cells.Select(cell => cell.Index));
            AssertInside(cells, 1200, 800);
            AssertNoOverlap(cells);
        }
    }

    [Fact]
    public void Odd_counts_are_placed_without_holes_in_the_order()
    {
        foreach (var layout in Layouts)
        {
            var cells = CollageMath.Cells(7, layout, 1000, 700, 8);

            Assert.Equal(7, cells.Count);
            Assert.Equal(Enumerable.Range(0, 7), cells.Select(cell => cell.Index));
            AssertNoOverlap(cells);
        }
    }

    [Fact]
    public void Featured_makes_the_first_cell_the_biggest()
    {
        var cells = CollageMath.Cells(4, CollageLayout.Featured, 1200, 800, 10);

        var featured = cells[0].Bounds;
        Assert.Equal(0, featured.X, Precision);
        Assert.Equal(800, featured.Height, Precision);
        Assert.Equal((1200 - 10) * CollageMath.FeaturedShare, featured.Width, Precision);

        var others = cells.Skip(1).Select(cell => cell.Bounds).ToList();
        Assert.All(others, cell => Assert.True(featured.Area > cell.Area * 2));
    }

    [Fact]
    public void Featured_with_a_single_image_uses_the_whole_canvas()
    {
        var cells = CollageMath.Cells(1, CollageLayout.Featured, 640, 480, 20);

        Assert.Single(cells);
        Assert.Equal(new CollageRect(0, 0, 640, 480), cells[0].Bounds);
    }

    [Fact]
    public void Fill_covers_the_cell_and_crops_the_overflow()
    {
        var cell = new CollageCell(0, new CollageRect(10, 20, 200, 100));

        // A square source into a 2:1 cell: the width is the limiting side, the source is cropped vertically.
        var square = CollageMath.Place(cell, 400, 400, CollageFit.Fill);
        Assert.Equal(cell.Bounds, square.Image);
        Assert.Equal(400, square.Source.Width, Precision);
        Assert.Equal(200, square.Source.Height, Precision);
        Assert.Equal(0, square.Source.X, Precision);
        Assert.Equal(100, square.Source.Y, Precision);

        // A 4:1 source: it is cropped horizontally.
        var wide = CollageMath.Place(cell, 400, 100, CollageFit.Fill);
        Assert.Equal(200, wide.Source.Width, Precision);
        Assert.Equal(100, wide.Source.Height, Precision);
        Assert.Equal(100, wide.Source.X, Precision);
        Assert.Equal(0, wide.Source.Y, Precision);
        // The source crop always matches the cell's aspect ratio.
        Assert.Equal(2, wide.Source.Width / wide.Source.Height, Precision);
    }

    [Fact]
    public void Fit_keeps_the_whole_source_and_letterboxes_inside_the_cell()
    {
        var cell = new CollageCell(0, new CollageRect(10, 20, 200, 200));

        var landscape = CollageMath.Place(cell, 400, 200, CollageFit.Fit);
        Assert.Equal(0, landscape.Source.X, Precision);
        Assert.Equal(400, landscape.Source.Width, Precision);
        Assert.Equal(200, landscape.Source.Height, Precision);
        Assert.Equal(200, landscape.Image.Width, Precision);
        Assert.Equal(100, landscape.Image.Height, Precision);
        Assert.Equal(70, landscape.Image.Y, Precision);

        var portrait = CollageMath.Place(cell, 100, 400, CollageFit.Fit);
        Assert.Equal(50, portrait.Image.Width, Precision);
        Assert.Equal(200, portrait.Image.Height, Precision);
        Assert.Equal(85, portrait.Image.X, Precision);
        Assert.Equal(20, portrait.Image.Y, Precision);
    }

    [Fact]
    public void Placements_cover_every_image_in_order()
    {
        var cells = CollageMath.Cells(3, CollageLayout.Grid, 1000, 600, 8);
        CollageSize[] images = [new(400, 300), new(300, 600), new(800, 200)];

        var placements = CollageMath.Placements(cells, images, CollageFit.Fit);

        Assert.Equal(3, placements.Count);
        Assert.Equal(new[] { 0, 1, 2 }, placements.Select(placement => placement.Index));
        for (var i = 0; i < placements.Count; i++)
        {
            Assert.Equal(cells[i].Bounds, placements[i].Cell);
            Assert.Equal(images[i].Width / (double)images[i].Height, placements[i].Image.Width / placements[i].Image.Height, 3);
            Assert.True(placements[i].Image.Width <= cells[i].Bounds.Width + 1e-6);
            Assert.True(placements[i].Image.Height <= cells[i].Bounds.Height + 1e-6);
        }
    }

    [Fact]
    public void Corner_radius_is_clamped_to_half_the_shorter_side()
    {
        var cell = new CollageRect(0, 0, 200, 100);

        Assert.Equal(50, CollageMath.CornerRadius(cell, 80), Precision);
        Assert.Equal(12, CollageMath.CornerRadius(cell, 12), Precision);
        Assert.Equal(0, CollageMath.CornerRadius(cell, -4), Precision);
    }

    [Fact]
    public void ClampSpacing_never_collapses_a_cell()
    {
        foreach (var layout in Layouts)
        {
            var gap = CollageMath.ClampSpacing(6, layout, 300, 200, 1000);
            var cells = CollageMath.Cells(6, layout, 300, 200, 1000);
            Assert.True(gap >= 0);
            Assert.All(cells, cell => Assert.True(cell.Bounds.Width > 0 && cell.Bounds.Height > 0));
        }
    }

    [Fact]
    public void Empty_input_has_no_cells()
    {
        foreach (var layout in Layouts)
            Assert.Empty(CollageMath.Cells(0, layout, 800, 600, 10));
    }

    [Fact]
    public void Presets_are_the_three_shipped_output_sizes()
    {
        Assert.Equal(new CollageSize(1080, 1080), CollagePresets.Square);
        Assert.Equal(new CollageSize(1920, 1080), CollagePresets.Landscape);
        Assert.Equal(new CollageSize(1080, 1920), CollagePresets.Portrait);
        Assert.Equal(3, CollagePresets.All.Count);
        Assert.Equal(1.0, Math.Round(CollagePresets.All[0].Aspect, 6));
        Assert.Equal(Math.Round(16.0 / 9.0, 6), Math.Round(CollagePresets.All[1].Aspect, 6));
        Assert.Equal(Math.Round(9.0 / 16.0, 6), Math.Round(CollagePresets.All[2].Aspect, 6));
    }

    [Fact]
    public void Output_size_is_clamped_to_the_supported_bounds()
    {
        Assert.Equal(new CollageSize(64, 64), CollagePresets.Clamp(1, -20));
        Assert.Equal(new CollageSize(8192, 8192), CollagePresets.Clamp(99999, 99999));
        Assert.Equal(new CollageSize(1280, 720), CollagePresets.Clamp(1280, 720));
    }

    [Fact]
    public void Order_starts_in_drop_order_and_moves_positions()
    {
        var order = new CollageOrder(4);

        Assert.Equal(new[] { 0, 1, 2, 3 }, order.SourceIndices);
        Assert.True(order.Move(0, 2));
        Assert.Equal(new[] { 1, 2, 0, 3 }, order.SourceIndices);
        Assert.Equal(2, order.PositionOf(0));
        Assert.False(order.Move(2, 2));
        Assert.False(order.Move(-1, 0));
        Assert.False(order.Move(0, 99));
    }

    [Fact]
    public void Order_move_by_stops_at_the_ends()
    {
        var order = new CollageOrder(3);

        Assert.True(order.MoveBy(1, -1));
        Assert.Equal(new[] { 1, 0, 2 }, order.SourceIndices);
        Assert.False(order.MoveBy(0, -1));
        Assert.True(order.MoveBy(1, 1));
        Assert.Equal(new[] { 1, 2, 0 }, order.SourceIndices);
        Assert.False(order.MoveBy(2, 3));
    }

    [Fact]
    public void Order_swaps_resets_and_applies()
    {
        var order = new CollageOrder(3);
        string[] names = ["a", "b", "c"];

        Assert.True(order.Swap(0, 2));
        Assert.Equal(new[] { 2, 1, 0 }, order.SourceIndices);
        Assert.Equal(new[] { "c", "b", "a" }, order.Apply(names));

        order.Reset();
        Assert.Equal(new[] { 0, 1, 2 }, order.SourceIndices);
        Assert.Equal(names, order.Apply(names));
        Assert.Throws<ArgumentException>(() => order.Apply(["only one"]));
    }

    [Fact]
    public void Order_rebuild_rejects_broken_permutations()
    {
        Assert.Equal(new[] { 2, 0, 1 }, CollageOrder.FromSourceOrder([2, 0, 1]).SourceIndices);
        Assert.Throws<ArgumentException>(() => CollageOrder.FromSourceOrder([0, 0, 2]));
        Assert.Throws<ArgumentException>(() => CollageOrder.FromSourceOrder([0, 1, 9]));
        Assert.Throws<ArgumentException>(() => CollageOrder.FromSourceOrder([0, -1, 2]));
    }

    [Fact]
    public void Clone_is_independent()
    {
        var order = new CollageOrder(3);
        order.Move(0, 2);
        var clone = order.Clone();

        clone.Move(0, 2);

        Assert.Equal(new[] { 1, 2, 0 }, order.SourceIndices);
        Assert.Equal(new[] { 2, 0, 1 }, clone.SourceIndices);
    }

    [Fact]
    public void Colours_round_trip_hex_and_mix()
    {
        var colour = CollageColor.FromHex("#8224D4");

        Assert.Equal(new CollageColor(0x82, 0x24, 0xD4), colour);
        Assert.Equal("#8224D4", colour.ToHex());
        Assert.Equal(new CollageColor(0xFF, 0xFF, 0xFF), colour.Mix(CollageColor.White, 1));
        Assert.Equal(colour, colour.Mix(CollageColor.White, 0));
        Assert.True(CollageColor.TryFromHex("fff", out var shortHex));
        Assert.Equal(CollageColor.White, shortHex);
        Assert.False(CollageColor.TryFromHex("not a colour", out _));
        Assert.Throws<FormatException>(() => CollageColor.FromHex("#12345"));
    }

    [Fact]
    public void Background_swatches_make_a_solid_colour_or_a_lighter_gradient()
    {
        var colour = CollageColor.FromHex("#8224D4");

        var solid = CollageBackground.For(colour, CollageBackgroundKind.Solid);
        Assert.Equal(CollageBackgroundKind.Solid, solid.Kind);
        Assert.Equal(colour, solid.Start);
        Assert.Equal(colour, solid.End);

        var gradient = CollageBackground.For(colour, CollageBackgroundKind.Gradient);
        Assert.Equal(CollageBackgroundKind.Gradient, gradient.Kind);
        Assert.Equal(colour, gradient.Start);
        Assert.True(gradient.End.R > colour.R && gradient.End.G > colour.G);
    }

    [Fact]
    public void Palette_is_the_five_documented_swatches()
    {
        Assert.Equal(5, CollagePalette.Colors.Count);
        Assert.Equal("#14121E", CollagePalette.Colors[0].ToHex());
        Assert.Equal("#FFFFFF", CollagePalette.Colors[1].ToHex());
    }

    [Fact]
    public async Task Renderer_writes_the_output_next_to_the_first_file_and_never_overwrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dropwheel-collage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.png");
            var second = Path.Combine(directory, "second.png");
            using (var image = new MagickImage(MagickColors.RebeccaPurple, 320, 200))
                image.Write(first);
            using (var image = new MagickImage(MagickColors.Goldenrod, 200, 320))
                image.Write(second);

            var options = new CollageOptions
            {
                Layout = CollageLayout.Grid,
                Fit = CollageFit.Fill,
                Size = new CollageSize(320, 240),
                Spacing = 4,
                Radius = 12,
                Background = CollageBackground.For(CollagePalette.Colors[2], CollageBackgroundKind.Gradient),
            };

            var output = await CollageRenderer.RenderAsync([first, second], options, directory);

            Assert.Equal(Path.Combine(directory, "Collage.png"), output);
            Assert.True(File.Exists(output));
            using (var rendered = new MagickImage(output))
            {
                Assert.Equal(320u, rendered.Width);
                Assert.Equal(240u, rendered.Height);
            }

            // A second run must pick a new name instead of replacing the first collage.
            var again = await CollageRenderer.RenderAsync([first, second], options, directory);
            Assert.Equal(Path.Combine(directory, "Collage (2).png"), again);
            Assert.True(File.Exists(again));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Renderer_places_the_images_in_the_grid_cells_with_the_background_in_the_gap()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dropwheel-collage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "purple.png");
            var second = Path.Combine(directory, "gold.png");
            using (var image = new MagickImage(MagickColors.RebeccaPurple, 100, 100))
                image.Write(first);
            using (var image = new MagickImage(MagickColors.Goldenrod, 100, 100))
                image.Write(second);

            var options = new CollageOptions
            {
                Layout = CollageLayout.Grid,
                Fit = CollageFit.Fill,
                Size = new CollageSize(320, 240),
                Spacing = 40,
                Radius = 0,
                Background = CollageBackground.Solid(CollageColor.Black),
            };

            var output = await CollageRenderer.RenderAsync([first, second], options, directory);
            using var rendered = new MagickImage(output);

            // Two 140-pixel cells with a 40-pixel gap: cell 0 is 0..140, the gap 140..180, cell 1 180..320.
            AssertColour(rendered, 70, 120, MagickColors.RebeccaPurple);
            AssertColour(rendered, 160, 120, MagickColors.Black);
            AssertColour(rendered, 250, 120, MagickColors.Goldenrod);
            AssertColour(rendered, 70, 5, MagickColors.RebeccaPurple);
            AssertColour(rendered, 315, 120, MagickColors.Goldenrod);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1080, 1080)]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]
    public async Task Renderer_honours_every_output_preset_and_leaves_the_source_files_untouched(int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dropwheel-collage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.png");
            var second = Path.Combine(directory, "second.jpg");
            using (var image = new MagickImage(MagickColors.RebeccaPurple, 400, 300))
                image.Write(first);
            using (var image = new MagickImage(MagickColors.Goldenrod, 300, 400))
                image.Write(second);

            var firstHash = SHA256.HashData(File.ReadAllBytes(first));
            var secondHash = SHA256.HashData(File.ReadAllBytes(second));
            var firstWrite = File.GetLastWriteTimeUtc(first);
            var secondWrite = File.GetLastWriteTimeUtc(second);

            var options = new CollageOptions
            {
                Layout = CollageLayout.Grid,
                Fit = CollageFit.Fill,
                Size = new CollageSize(width, height),
                Spacing = 8,
                Radius = 16,
                Background = CollageBackground.Solid(CollagePalette.Colors[0]),
            };

            var output = await CollageRenderer.RenderAsync([first, second], options, directory);

            Assert.True(File.Exists(output));
            Assert.Equal("png", Path.GetExtension(output).TrimStart('.'));
            using (var rendered = new MagickImage(output))
            {
                Assert.Equal((uint)width, rendered.Width);
                Assert.Equal((uint)height, rendered.Height);
            }

            // The renderer reads the originals only: same bytes, same write timestamps.
            Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(first)));
            Assert.Equal(secondHash, SHA256.HashData(File.ReadAllBytes(second)));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(first));
            Assert.Equal(secondWrite, File.GetLastWriteTimeUtc(second));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Window_probe_keeps_the_designed_size_and_fits_a_small_work_area()
    {
        var desktop = ToolWindowSizer.Compute(CollageTool.DefaultLayout, 1920, 1080, 1920, 1080, 1.0);
        Assert.Equal(560, desktop.Width, 3);
        Assert.Equal(747.5, desktop.Height, 3);

        var small = ToolWindowSizer.Compute(CollageTool.DefaultLayout, 1920, 1080, 1366, 768, 1.0);
        Assert.True(small.Width <= 1366 * ToolWindowSizer.MaxWidthRatio + 0.01, $"width {small.Width}");
        Assert.True(small.Height <= 768 * ToolWindowSizer.MaxHeightRatio + 0.01, $"height {small.Height}");
    }

    [Fact]
    public void Primary_label_is_create_collage_in_both_tables()
    {
        Assert.Equal("tools.collage", CollageTool.Instance.Id);
        Assert.Equal("tool.collage.create", CollageTool.Instance.PrimaryLabelKey);
        Assert.Equal("Create Collage", Loc.Table(Language.English)[CollageTool.Instance.PrimaryLabelKey]);
        Assert.NotEqual("Create Collage", Loc.Table(Language.Arabic)[CollageTool.Instance.PrimaryLabelKey]);
        Assert.True(Loc.HasKey(CollageTool.Instance.PrimaryLabelKey, Language.English));
        Assert.True(Loc.HasKey(CollageTool.Instance.PrimaryLabelKey, Language.Arabic));
    }

    [Fact]
    public void Theme_switches_between_dark_and_light_palettes_for_the_tool()
    {
        var previous = Theme.Override;
        try
        {
            Theme.Override = true;
            Theme.Refresh();
            Assert.True(Theme.IsDark);
            var darkCard = ((SolidColorBrush)Theme.Card).Color;

            Theme.Override = false;
            Theme.Refresh();
            Assert.False(Theme.IsDark);
            var lightCard = ((SolidColorBrush)Theme.Card).Color;

            Assert.NotEqual(darkCard, lightCard);
        }
        finally
        {
            Theme.Override = previous;
            Theme.Refresh();
        }
    }

    private static void AssertColour(MagickImage image, int x, int y, IMagickColor<byte> expected, int tolerance = 1)
    {
        using var pixels = image.GetPixels();
        var actual = pixels.GetPixel(x, y).ToColor();
        Assert.InRange((int)actual.R, Math.Max(0, (int)expected.R - tolerance), Math.Min(255, (int)expected.R + tolerance));
        Assert.InRange((int)actual.G, Math.Max(0, (int)expected.G - tolerance), Math.Min(255, (int)expected.G + tolerance));
        Assert.InRange((int)actual.B, Math.Max(0, (int)expected.B - tolerance), Math.Min(255, (int)expected.B + tolerance));
    }

    private static void AssertInside(IReadOnlyList<CollageCell> cells, double width, double height)
    {
        foreach (var cell in cells)
        {
            Assert.True(cell.Bounds.X >= -1e-6, $"cell {cell.Index} left {cell.Bounds.X}");
            Assert.True(cell.Bounds.Y >= -1e-6, $"cell {cell.Index} top {cell.Bounds.Y}");
            Assert.True(cell.Bounds.Right <= width + 1e-6, $"cell {cell.Index} right {cell.Bounds.Right}");
            Assert.True(cell.Bounds.Bottom <= height + 1e-6, $"cell {cell.Index} bottom {cell.Bounds.Bottom}");
            Assert.True(cell.Bounds.Width > 0 && cell.Bounds.Height > 0, $"cell {cell.Index} is empty");
        }
    }

    private static void AssertNoOverlap(IReadOnlyList<CollageCell> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            for (var j = i + 1; j < cells.Count; j++)
            {
                var a = cells[i].Bounds;
                var b = cells[j].Bounds;
                var overlapWidth = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
                var overlapHeight = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
                Assert.True(
                    overlapWidth <= 1e-6 || overlapHeight <= 1e-6,
                    $"cells {i} and {j} overlap by {overlapWidth} x {overlapHeight}");
            }
        }
    }
}
