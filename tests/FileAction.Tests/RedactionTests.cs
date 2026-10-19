using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure redaction rules behind the Redact tool: boxes, hit-testing, moving/resizing, the flatten plan
/// (global and per-area modes at original resolution), the downsampled preview plan and the OCR line
/// geometry. The window and the file writer only do what these functions say, so the interesting cases are
/// all covered here without a UI or a bitmap.
/// </summary>
public class RedactionTests
{
    // ---------------------------------------------------------------- boxes

    [Fact]
    public void Box_reports_its_edges_and_hits_its_own_border()
    {
        var box = new RedactionBox(10, 20, 30, 40);

        Assert.Equal(40, box.Right, 3);
        Assert.Equal(60, box.Bottom, 3);
        Assert.False(box.IsEmpty);
        Assert.True(box.Contains(10, 20));
        Assert.True(box.Contains(40, 60));
        Assert.True(box.Contains(25, 40));
        Assert.False(box.Contains(9.9, 40));
        Assert.False(box.Contains(25, 60.1));
    }

    [Fact]
    public void Empty_box_covers_nothing()
    {
        Assert.True(new RedactionBox(0, 0, 0, 10).IsEmpty);
        Assert.True(new RedactionBox(0, 0, 10, -5).IsEmpty);
        Assert.True(new RedactionBox(0, 0, double.NaN, 10).IsEmpty);
        Assert.False(new RedactionBox(0, 0, 0.5, 0.5).IsEmpty);

        // A broken position is not an empty size; Snap rejects it instead of silently moving the area.
        Assert.False(new RedactionBox(double.NaN, 0, 10, 10).IsEmpty);
        Assert.Null(RedactionPlan.Snap(new RedactionBox(double.NaN, 0, 10, 10), 100, 100));
    }

    [Fact]
    public void Boxes_intersect_only_when_they_share_area()
    {
        var box = new RedactionBox(0, 0, 10, 10);

        Assert.True(box.Intersects(new RedactionBox(5, 5, 10, 10)));
        Assert.False(box.Intersects(new RedactionBox(10, 0, 10, 10)));
        Assert.False(box.Intersects(new RedactionBox(0, 10, 10, 10)));
        Assert.False(box.Intersects(new RedactionBox(50, 50, 10, 10)));
    }

    [Fact]
    public void FromEdges_never_returns_a_negative_size()
    {
        var box = RedactionBox.FromEdges(40, 50, 10, 20);

        Assert.Equal(10, box.X, 3);
        Assert.Equal(20, box.Y, 3);
        Assert.True(box.IsEmpty);
    }

    [Fact]
    public void Colour_channels_round_trip()
    {
        var color = RedactionColors.FromArgb(0xFF, 0x12, 0x34, 0x56);

        Assert.Equal(0xFF123456u, color);
        var (a, r, g, b) = RedactionColors.Split(color);
        Assert.Equal(0xFF, a);
        Assert.Equal(0x12, r);
        Assert.Equal(0x34, g);
        Assert.Equal(0x56, b);
    }

    [Fact]
    public void Default_style_is_solid_black()
    {
        Assert.Equal(RedactionMode.Solid, RedactionStyle.Default.Mode);
        Assert.Equal(RedactionColors.Black, RedactionStyle.Default.Color);
        Assert.InRange(RedactionStyle.Default.Strength, RedactionPlan.MinStrength, RedactionPlan.MaxStrength);
    }

    // ---------------------------------------------------------------- hit testing

    [Fact]
    public void HitTest_returns_the_topmost_area()
    {
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(0, 0, 100, 100)),
            new(new RedactionBox(20, 20, 30, 30)),
        };

        Assert.Equal(1, RedactionEditor.HitTest(areas, 30, 30));
        Assert.Equal(0, RedactionEditor.HitTest(areas, 80, 80));
        Assert.Equal(-1, RedactionEditor.HitTest(areas, 200, 200));
    }

    [Fact]
    public void HitTest_of_nothing_is_minus_one()
    {
        Assert.Equal(-1, RedactionEditor.HitTest([], 10, 10));
    }

    [Fact]
    public void HandleAt_finds_every_handle_at_its_own_point()
    {
        var box = new RedactionBox(100, 100, 200, 100);

        foreach (var handle in Enum.GetValues<RedactionHandle>().Where(h => h != RedactionHandle.None))
        {
            var (x, y) = RedactionEditor.HandlePoint(box, handle);
            Assert.Equal(handle, RedactionEditor.HandleAt(box, x, y, tolerance: 4));
        }
    }

    [Fact]
    public void HandleAt_misses_points_far_from_every_handle()
    {
        var box = new RedactionBox(0, 0, 100, 100);

        Assert.Equal(RedactionHandle.None, RedactionEditor.HandleAt(box, 50, 50, tolerance: 6));
        Assert.Equal(RedactionHandle.None, RedactionEditor.HandleAt(box, 49, 49, tolerance: -1));
        Assert.Equal(RedactionHandle.None, RedactionEditor.HandleAt(default, 0, 0, tolerance: 6));
    }

    [Fact]
    public void HandleAt_prefers_the_nearest_handle_on_a_tiny_area()
    {
        var box = new RedactionBox(0, 0, 2, 2);

        // All four corners are within tolerance; the call must still resolve to exactly one handle.
        Assert.NotEqual(RedactionHandle.None, RedactionEditor.HandleAt(box, 1, 1, tolerance: 10));
    }

    // ---------------------------------------------------------------- create / move / resize

    [Fact]
    public void Create_normalizes_the_drag_direction()
    {
        var rightDown = RedactionEditor.Create(10, 20, 60, 80, 200, 200);
        var leftUp = RedactionEditor.Create(60, 80, 10, 20, 200, 200);

        Assert.Equal(rightDown, leftUp);
        Assert.Equal(10, rightDown.X, 3);
        Assert.Equal(20, rightDown.Y, 3);
        Assert.Equal(50, rightDown.Width, 3);
        Assert.Equal(60, rightDown.Height, 3);
    }

    [Fact]
    public void Create_clamps_the_drag_to_the_image()
    {
        var box = RedactionEditor.Create(-40, -40, 500, 500, 100, 80);

        Assert.Equal(0, box.X, 3);
        Assert.Equal(0, box.Y, 3);
        Assert.Equal(100, box.Width, 3);
        Assert.Equal(80, box.Height, 3);
    }

    [Fact]
    public void Create_needs_a_real_image()
    {
        Assert.True(RedactionEditor.Create(0, 0, 10, 10, 0, 100).IsEmpty);
        Assert.True(RedactionEditor.Create(double.NaN, 0, 10, 10, 100, 100).IsEmpty);
    }

    [Fact]
    public void Move_keeps_the_area_inside_the_image()
    {
        var box = new RedactionBox(10, 10, 50, 40);

        var moved = RedactionEditor.Move(box, -100, 1000, 200, 200);

        Assert.Equal(0, moved.X, 3);
        Assert.Equal(160, moved.Y, 3);
        Assert.Equal(box.Width, moved.Width, 3);
        Assert.Equal(box.Height, moved.Height, 3);
    }

    [Fact]
    public void Move_by_a_delta_can_be_undone()
    {
        var box = new RedactionBox(10, 20, 50, 40);

        var forward = RedactionEditor.Move(box, 30, 15, 400, 400);
        var back = RedactionEditor.Move(forward, -30, -15, 400, 400);

        Assert.Equal(box, back);
    }

    [Fact]
    public void Move_an_empty_box_changes_nothing()
    {
        var box = new RedactionBox(0, 0, 0, 0);

        Assert.Equal(box, RedactionEditor.Move(box, 10, 10, 100, 100));
    }

    [Fact]
    public void Resize_from_the_bottom_right_grows_the_area()
    {
        var start = new RedactionBox(100, 100, 200, 100);

        var resized = RedactionEditor.Resize(RedactionHandle.BottomRight, start, 50, 25, 800, 600);

        Assert.Equal(100, resized.X, 3);
        Assert.Equal(100, resized.Y, 3);
        Assert.Equal(250, resized.Width, 3);
        Assert.Equal(125, resized.Height, 3);
    }

    [Fact]
    public void Resize_from_the_top_left_anchors_the_bottom_right()
    {
        var start = new RedactionBox(100, 100, 200, 100);

        var resized = RedactionEditor.Resize(RedactionHandle.TopLeft, start, 20, 10, 800, 600);

        Assert.Equal(120, resized.X, 3);
        Assert.Equal(110, resized.Y, 3);
        Assert.Equal(180, resized.Width, 3);
        Assert.Equal(90, resized.Height, 3);
        Assert.Equal(start.Right, resized.Right, 3);
        Assert.Equal(start.Bottom, resized.Bottom, 3);
    }

    [Fact]
    public void Resize_never_goes_below_the_minimum_size()
    {
        var start = new RedactionBox(100, 100, 200, 200);

        var resized = RedactionEditor.Resize(RedactionHandle.BottomRight, start, -500, -500, 800, 600);

        Assert.True(resized.Width >= RedactionEditor.MinSize);
        Assert.True(resized.Height >= RedactionEditor.MinSize);
        Assert.Equal(start.X, resized.X, 3);
        Assert.Equal(start.Y, resized.Y, 3);
    }

    [Fact]
    public void Resize_is_clamped_to_the_image()
    {
        var start = new RedactionBox(10, 10, 50, 50);

        var resized = RedactionEditor.Resize(RedactionHandle.TopLeft, start, -500, -500, 100, 100);

        Assert.Equal(0, resized.X, 3);
        Assert.Equal(0, resized.Y, 3);
        Assert.Equal(60, resized.Width, 3);
        Assert.Equal(60, resized.Height, 3);
    }

    [Fact]
    public void Resize_without_a_handle_returns_the_start_box()
    {
        var start = new RedactionBox(10, 10, 50, 50);

        Assert.Equal(start, RedactionEditor.Resize(RedactionHandle.None, start, 10, 10, 100, 100));
    }

    // ---------------------------------------------------------------- z-order

    [Fact]
    public void BringToFront_moves_only_the_selected_area()
    {
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(0, 0, 10, 10)),
            new(new RedactionBox(10, 0, 10, 10)),
            new(new RedactionBox(20, 0, 10, 10)),
        };

        var index = RedactionEditor.BringToFront(areas, 0);

        Assert.Equal(2, index);
        Assert.Equal(10, areas[0].Box.X, 3);
        Assert.Equal(20, areas[1].Box.X, 3);
        Assert.Equal(0, areas[2].Box.X, 3);
    }

    [Fact]
    public void BringToFront_leaves_the_last_area_and_bad_indices_alone()
    {
        var areas = new List<RedactionArea> { new(new RedactionBox(0, 0, 10, 10)) };

        Assert.Equal(0, RedactionEditor.BringToFront(areas, 0));
        Assert.Equal(-1, RedactionEditor.BringToFront(areas, -1));
        Assert.Equal(7, RedactionEditor.BringToFront(areas, 7));
        Assert.Single(areas);
    }

    // ---------------------------------------------------------------- flatten plan

    [Fact]
    public void Plan_keeps_the_paint_order()
    {
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(10, 10, 20, 20)),
            new(new RedactionBox(30, 30, 20, 20)),
        };

        var plan = RedactionPlan.Build(100, 100, areas, RedactionStyle.Default);

        Assert.Equal(2, plan.Count);
        Assert.Equal(10, plan[0].X);
        Assert.Equal(30, plan[1].X);
    }

    [Fact]
    public void Plan_resolves_the_global_mode_and_per_area_overrides()
    {
        var style = RedactionStyle.Default with { Mode = RedactionMode.Solid };
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(0, 0, 10, 10)),
            new(new RedactionBox(20, 0, 10, 10), Mode: RedactionMode.Blur),
            new(new RedactionBox(40, 0, 10, 10), Mode: RedactionMode.Pixelate),
        };

        var plan = RedactionPlan.Build(100, 100, areas, style);

        Assert.Equal(RedactionMode.Solid, plan[0].Mode);
        Assert.Equal(RedactionMode.Blur, plan[1].Mode);
        Assert.Equal(RedactionMode.Pixelate, plan[2].Mode);
        Assert.True(plan[1].BlurSigma > 0);
        Assert.Equal(0, plan[0].BlurSigma, 3);
        Assert.Equal(1, plan[0].PixelBlock);
        Assert.Equal(1, plan[1].PixelBlock);
        Assert.True(plan[2].PixelBlock > 1);
    }

    [Fact]
    public void Plan_resolves_the_strength_and_clamps_it()
    {
        var style = RedactionStyle.Default with { Mode = RedactionMode.Blur, Strength = 40 };
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(0, 0, 10, 10)),
            new(new RedactionBox(20, 0, 10, 10), Strength: 90),
            new(new RedactionBox(40, 0, 10, 10), Strength: 500),
        };

        var plan = RedactionPlan.Build(100, 100, areas, style);

        Assert.Equal(RedactionPlan.BlurSigma(40), plan[0].BlurSigma, 3);
        Assert.Equal(RedactionPlan.BlurSigma(90), plan[1].BlurSigma, 3);
        Assert.Equal(RedactionPlan.BlurSigma(RedactionPlan.MaxStrength), plan[2].BlurSigma, 3);
    }

    [Fact]
    public void Plan_carries_the_global_colour()
    {
        var style = new RedactionStyle(RedactionMode.Solid, 25, RedactionColors.FromArgb(255, 255, 0, 0));
        var plan = RedactionPlan.Build(10, 10, [new RedactionArea(new RedactionBox(0, 0, 5, 5))], style);

        Assert.Equal(style.Color, Assert.Single(plan).Color);
    }

    [Fact]
    public void Plan_snaps_boxes_to_pixels_and_clamps_them()
    {
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(10.4, 20.6, 30.2, 40.7)),
            new(new RedactionBox(-40, -40, 400, 400)),
        };

        var plan = RedactionPlan.Build(100, 100, areas, RedactionStyle.Default);

        Assert.Equal((10, 21, 31, 40), (plan[0].X, plan[0].Y, plan[0].Width, plan[0].Height));
        Assert.Equal((0, 0, 100, 100), (plan[1].X, plan[1].Y, plan[1].Width, plan[1].Height));
    }

    [Fact]
    public void Plan_drops_boxes_that_cover_no_pixel()
    {
        var areas = new List<RedactionArea>
        {
            new(new RedactionBox(0, 0, 0.2, 20)),
            new(new RedactionBox(50, 50, 20, 0.2)),
            new(default),
            null!,
        };

        Assert.Empty(RedactionPlan.Build(100, 100, areas, RedactionStyle.Default));
    }

    [Fact]
    public void Plan_of_an_empty_image_or_empty_list_is_empty()
    {
        Assert.Empty(RedactionPlan.Build(0, 100, [new RedactionArea(new RedactionBox(0, 0, 10, 10))], RedactionStyle.Default));
        Assert.Empty(RedactionPlan.Build(100, 100, [], RedactionStyle.Default));
    }

    [Fact]
    public void Strength_derivations_are_monotonic_and_bounded()
    {
        Assert.True(RedactionPlan.BlurSigma(1) >= 1);
        Assert.True(RedactionPlan.BlurSigma(100) > RedactionPlan.BlurSigma(50));
        Assert.Equal(RedactionPlan.BlurSigma(100), RedactionPlan.BlurSigma(1000), 3);
        Assert.True(RedactionPlan.PixelBlock(1) >= 2);
        Assert.True(RedactionPlan.PixelBlock(100) > RedactionPlan.PixelBlock(50));
        Assert.Equal(RedactionPlan.PixelBlock(100), RedactionPlan.PixelBlock(1000));
    }

    [Fact]
    public void Snap_reports_the_rounded_integer_rectangle()
    {
        var rect = RedactionPlan.Snap(new RedactionBox(10.4, 20.6, 30.2, 40.7), 100, 100);

        Assert.Equal(new RedactionRect(10, 21, 31, 40), rect);
        Assert.Null(RedactionPlan.Snap(new RedactionBox(10, 10, 0.1, 0.1), 100, 100));
        Assert.Null(RedactionPlan.Snap(new RedactionBox(10, 10, 5, 5), 0, 100));
    }

    // ---------------------------------------------------------------- preview plan

    [Fact]
    public void Preview_plan_scales_boxes_and_effect_parameters()
    {
        var style = new RedactionStyle(RedactionMode.Blur, 40, RedactionColors.Black);
        var plan = RedactionPlan.Build(1000, 1000, [new RedactionArea(new RedactionBox(100, 100, 200, 100))], style);

        var preview = RedactionPlan.ToPreview(plan, 250, 250, 0.25);

        var step = Assert.Single(preview);
        Assert.Equal((25, 25, 50, 25), (step.X, step.Y, step.Width, step.Height));
        Assert.Equal(plan[0].BlurSigma * 0.25, step.BlurSigma, 3);
    }

    [Fact]
    public void Preview_plan_scales_the_pixel_block_and_drops_vanished_areas()
    {
        var style = new RedactionStyle(RedactionMode.Pixelate, 20, RedactionColors.Black);
        var plan = RedactionPlan.Build(1000, 1000, [new RedactionArea(new RedactionBox(100, 100, 200, 100))], style);

        var preview = RedactionPlan.ToPreview(plan, 50, 50, 0.05);
        Assert.Equal(1, Assert.Single(preview).PixelBlock);

        Assert.Empty(RedactionPlan.ToPreview(plan, 10, 10, 0));
        Assert.Empty(RedactionPlan.ToPreview(plan, 10, 10, double.NaN));
    }

    // ---------------------------------------------------------------- OCR line geometry

    [Fact]
    public void Union_spans_every_word_on_the_line()
    {
        var union = RedactionText.Union(
        [
            new RedactionBox(10, 10, 20, 12),
            new RedactionBox(35, 11, 15, 11),
            new RedactionBox(55, 9, 25, 14),
        ]);

        Assert.Equal(10, union.X, 3);
        Assert.Equal(9, union.Y, 3);
        Assert.Equal(70, union.Width, 3);
        Assert.Equal(14, union.Height, 3);
    }

    [Fact]
    public void Union_ignores_empty_boxes_and_has_no_origin_of_its_own()
    {
        Assert.True(RedactionText.Union([]).IsEmpty);
        Assert.True(RedactionText.Union([default]).IsEmpty);

        var union = RedactionText.Union([default, new RedactionBox(5, 6, 7, 8)]);
        Assert.Equal(new RedactionBox(5, 6, 7, 8), union);
    }

    [Fact]
    public void Pad_grows_a_box_and_never_returns_a_negative_one()
    {
        var padded = RedactionText.Pad(new RedactionBox(20, 20, 40, 30), 4);

        Assert.Equal(new RedactionBox(16, 16, 48, 38), padded);
        Assert.True(RedactionText.Pad(new RedactionBox(20, 20, 40, 30), -100).IsEmpty);
        Assert.True(RedactionText.Pad(new RedactionBox(20, 20, 40, 30), double.NaN) ==
                    new RedactionBox(20, 20, 40, 30));
    }
}
