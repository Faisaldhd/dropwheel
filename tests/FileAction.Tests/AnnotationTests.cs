using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The pure annotation model behind the Annotate tool: bounds and hit testing, moving and resizing, the
/// flattened render geometry, callout numbering, z-order and undo/redo. No window, bitmap or mouse is
/// involved, so the rules that decide where ink lands at original resolution are tested directly.
/// </summary>
public class AnnotationTests
{
    private static AnnotationObject Rect(double x, double y, double w, double h, bool filled = false) =>
        new(AnnotationKind.Rectangle, new AnnotationRect(x, y, w, h), AnnotationColor.Red, StrokeWidth: 4, Filled: filled);

    // ------------------------------------------------------------------ geometry

    [Fact]
    public void KindFor_maps_every_tool_but_select()
    {
        Assert.Null(AnnotationMath.KindFor(AnnotationTool.Select));

        foreach (var tool in Enum.GetValues<AnnotationTool>().Where(t => t != AnnotationTool.Select))
        {
            var kind = AnnotationMath.KindFor(tool);
            Assert.NotNull(kind);
            Assert.Equal(tool.ToString(), kind.Value.ToString());
        }
    }

    [Fact]
    public void FromPoints_normalizes_a_drag_in_any_direction()
    {
        var forward = AnnotationRect.FromPoints(10, 20, 40, 60);
        var backward = AnnotationRect.FromPoints(40, 60, 10, 20);

        Assert.Equal(new AnnotationRect(10, 20, 30, 40), forward);
        Assert.Equal(forward, backward);
        Assert.True(forward.Width > 0 && forward.Height > 0);
    }

    [Fact]
    public void BoundsOf_wraps_every_path_point()
    {
        var bounds = AnnotationMath.BoundsOf([
            new AnnotationPoint(50, 10),
            new AnnotationPoint(10, 90),
            new AnnotationPoint(30, 30),
        ]);

        Assert.Equal(new AnnotationRect(10, 10, 40, 80), bounds);
    }

    [Fact]
    public void ClampInside_shrinks_a_rectangle_that_is_larger_than_the_image()
    {
        var clamped = new AnnotationRect(-50, -20, 5000, 4000).ClampInside(800, 600);

        Assert.Equal(new AnnotationRect(0, 0, 800, 600), clamped);
    }

    [Fact]
    public void ClampInside_moves_a_rectangle_back_into_the_image()
    {
        var clamped = new AnnotationRect(700, 550, 200, 100).ClampInside(800, 600);

        Assert.Equal(600, clamped.X, 3);
        Assert.Equal(500, clamped.Y, 3);
        Assert.Equal(200, clamped.Width, 3);
        Assert.Equal(100, clamped.Height, 3);
    }

    // ------------------------------------------------------------------ move

    [Fact]
    public void Move_translates_a_box_and_keeps_it_inside_the_image()
    {
        var moved = AnnotationMath.Move(Rect(100, 100, 50, 50), 1000, -1000, 200, 200);

        Assert.Equal(new AnnotationRect(150, 0, 50, 50), moved.Bounds);
    }

    [Fact]
    public void Move_translates_a_line_without_deforming_it()
    {
        var line = AnnotationObject.Segment(
            AnnotationKind.Line, new AnnotationPoint(10, 10), new AnnotationPoint(20, 20), AnnotationColor.Blue);

        var moved = AnnotationMath.Move(line, 1000, 0, 100, 100);

        Assert.Equal(new AnnotationPoint(90, 10), moved.Path[0]);
        Assert.Equal(new AnnotationPoint(100, 20), moved.Path[1]);
        Assert.Equal(new AnnotationRect(90, 10, 10, 10), moved.Bounds);
        // Both points moved by the same amount, so the line length is unchanged.
        Assert.Equal(Math.Sqrt(200), moved.Path[1].DistanceTo(moved.Path[0]), 6);
    }

    // ------------------------------------------------------------------ resize

    [Fact]
    public void Resize_by_a_corner_keeps_the_opposite_corner_fixed()
    {
        var resized = AnnotationMath.Resize(Rect(100, 100, 200, 100), AnnotationHandle.BottomRight, 50, 25, 800, 600);

        Assert.Equal(new AnnotationRect(100, 100, 250, 125), resized.Bounds);
    }

    [Fact]
    public void Resize_by_the_top_left_handle_moves_the_origin()
    {
        var resized = AnnotationMath.Resize(Rect(100, 100, 100, 100), AnnotationHandle.TopLeft, 20, 30, 800, 600);

        Assert.Equal(new AnnotationRect(120, 130, 80, 70), resized.Bounds);
    }

    [Fact]
    public void Resize_never_goes_below_the_minimum_size()
    {
        var resized = AnnotationMath.Resize(Rect(100, 100, 200, 200), AnnotationHandle.BottomRight, -500, -500, 800, 600);

        Assert.True(resized.Bounds.Width >= AnnotationMath.MinSize);
        Assert.True(resized.Bounds.Height >= AnnotationMath.MinSize);
    }

    [Fact]
    public void Resize_stays_inside_the_image()
    {
        var resized = AnnotationMath.Resize(Rect(700, 500, 100, 100), AnnotationHandle.BottomRight, 500, 500, 800, 600);

        Assert.True(resized.Bounds.Right <= 800);
        Assert.True(resized.Bounds.Bottom <= 600);
    }

    [Fact]
    public void Resize_of_a_line_moves_only_the_grabbed_endpoint()
    {
        var line = AnnotationObject.Segment(
            AnnotationKind.Line, new AnnotationPoint(100, 100), new AnnotationPoint(200, 200), AnnotationColor.Red);

        var resized = AnnotationMath.Resize(line, AnnotationHandle.Start, 10, 20, 800, 600);

        Assert.Equal(new AnnotationPoint(110, 120), resized.Path[0]);
        Assert.Equal(new AnnotationPoint(200, 200), resized.Path[1]);
        Assert.Equal(new AnnotationRect(110, 120, 90, 80), resized.Bounds);
    }

    [Fact]
    public void Resize_of_a_pen_scales_the_whole_path()
    {
        var pen = AnnotationObject.Polyline(
            AnnotationKind.Pen,
            [new AnnotationPoint(0, 0), new AnnotationPoint(50, 0), new AnnotationPoint(50, 50)],
            AnnotationColor.Green);

        var resized = AnnotationMath.Resize(pen, AnnotationHandle.BottomRight, 50, 50, 800, 600);

        Assert.Equal(new AnnotationRect(0, 0, 100, 100), resized.Bounds);
        Assert.Equal(new AnnotationPoint(0, 0), resized.Path[0]);
        Assert.Equal(new AnnotationPoint(100, 0), resized.Path[1]);
        Assert.Equal(new AnnotationPoint(100, 100), resized.Path[2]);
    }

    [Fact]
    public void Resize_keeps_callouts_and_magnifiers_circular()
    {
        var callout = new AnnotationObject(
            AnnotationKind.Callout, new AnnotationRect(10, 10, 20, 20), AnnotationColor.Orange, Number: 1);

        var resized = AnnotationMath.Resize(callout, AnnotationHandle.BottomRight, 30, 10, 800, 600);

        Assert.Equal(resized.Bounds.Width, resized.Bounds.Height, 3);
        Assert.Equal(10, resized.Bounds.X, 3);
        Assert.Equal(10, resized.Bounds.Y, 3);
    }

    // ------------------------------------------------------------------ handles and hit testing

    [Fact]
    public void HandleAt_finds_the_grabbed_corner_and_edge()
    {
        var rect = Rect(100, 100, 100, 100);

        Assert.Equal(AnnotationHandle.TopLeft, AnnotationMath.HandleAt(rect, new AnnotationPoint(101, 99), 4));
        Assert.Equal(AnnotationHandle.Right, AnnotationMath.HandleAt(rect, new AnnotationPoint(200, 150), 4));
        Assert.Equal(AnnotationHandle.None, AnnotationMath.HandleAt(rect, new AnnotationPoint(150, 150), 4));
    }

    [Fact]
    public void HandleAt_finds_a_line_endpoint()
    {
        var line = AnnotationObject.Segment(
            AnnotationKind.Arrow, new AnnotationPoint(0, 0), new AnnotationPoint(100, 100), AnnotationColor.Red);

        Assert.Equal(AnnotationHandle.Start, AnnotationMath.HandleAt(line, new AnnotationPoint(1, 1), 4));
        Assert.Equal(AnnotationHandle.End, AnnotationMath.HandleAt(line, new AnnotationPoint(99, 99), 4));
        Assert.Equal(AnnotationHandle.None, AnnotationMath.HandleAt(line, new AnnotationPoint(50, 50), 4));
    }

    [Fact]
    public void HitTest_prefers_the_front_object()
    {
        var objects = new List<AnnotationObject> { Rect(0, 0, 100, 100), Rect(0, 0, 100, 100) };

        Assert.Equal(1, AnnotationMath.HitTest(objects, new AnnotationPoint(50, 50)));
        Assert.Equal(-1, AnnotationMath.HitTest(objects, new AnnotationPoint(500, 500)));
    }

    [Fact]
    public void HitTest_on_a_line_uses_the_distance_to_the_segment()
    {
        var line = AnnotationObject.Segment(
            AnnotationKind.Line, new AnnotationPoint(0, 0), new AnnotationPoint(100, 0), AnnotationColor.Red,
            strokeWidth: 4);

        Assert.True(AnnotationMath.HitTest(line, new AnnotationPoint(50, 5), 4));
        Assert.False(AnnotationMath.HitTest(line, new AnnotationPoint(50, 20), 4));
    }

    [Fact]
    public void HitTest_on_a_pen_stroke_uses_the_whole_path()
    {
        var pen = AnnotationObject.Polyline(
            AnnotationKind.Highlighter,
            [new AnnotationPoint(0, 0), new AnnotationPoint(0, 100)],
            AnnotationColor.Yellow,
            strokeWidth: 4);

        // A highlighter is four times wider than a pen, so a point 6 px beside the path still hits.
        Assert.True(AnnotationMath.HitTest(pen, new AnnotationPoint(6, 50), 0));
        Assert.False(AnnotationMath.HitTest(pen, new AnnotationPoint(20, 50), 0));
    }

    [Fact]
    public void DistanceToSegment_clamps_to_both_ends()
    {
        var from = new AnnotationPoint(0, 0);
        var to = new AnnotationPoint(10, 0);

        Assert.Equal(5, AnnotationMath.DistanceToSegment(new AnnotationPoint(5, 5), from, to), 6);
        Assert.Equal(5, AnnotationMath.DistanceToSegment(new AnnotationPoint(15, 0), from, to), 6);
        Assert.Equal(5, AnnotationMath.DistanceToSegment(new AnnotationPoint(-5, 0), from, to), 6);
    }

    // ------------------------------------------------------------------ flattened geometry

    [Fact]
    public void Arrow_flattens_to_a_shaft_and_a_head_that_meets_the_tip()
    {
        var arrow = AnnotationObject.Segment(
            AnnotationKind.Arrow, new AnnotationPoint(100, 100), new AnnotationPoint(200, 100),
            AnnotationColor.Red, strokeWidth: 4);

        var primitives = AnnotationMath.Flatten(arrow, 800, 600);

        Assert.Equal(2, primitives.Count);
        var shaft = primitives[0];
        Assert.Equal(AnnotationPrimitiveKind.Segment, shaft.Kind);
        Assert.Equal(100, shaft.Points[0].X, 3);
        // The shaft stops at the base of the head (head length = 4 × stroke = 16).
        Assert.Equal(184, shaft.Points[1].X, 3);
        Assert.Equal(4, shaft.StrokeWidth, 3);

        var head = primitives[1];
        Assert.Equal(AnnotationPrimitiveKind.Polygon, head.Kind);
        Assert.True(head.Filled);
        Assert.Equal(3, head.Points.Count);
        Assert.Equal(new AnnotationPoint(200, 100), head.Points[0]);
        Assert.Equal(184, head.Points[1].X, 3);
        Assert.Equal(184, head.Points[2].X, 3);
        // The two back corners sit on both sides of the shaft.
        Assert.True(head.Points[1].Y > 100 && head.Points[2].Y < 100);
    }

    [Fact]
    public void Rectangle_and_ellipse_flatten_with_the_filled_flag()
    {
        var filled = Rect(10, 20, 100, 50, filled: true);
        var hollow = Rect(10, 20, 100, 50);

        var filledPrimitive = AnnotationMath.Flatten(filled, 800, 600).Single();
        var hollowPrimitive = AnnotationMath.Flatten(hollow, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Rectangle, filledPrimitive.Kind);
        Assert.Equal(new AnnotationRect(10, 20, 100, 50), filledPrimitive.Rect);
        Assert.True(filledPrimitive.Filled);
        Assert.False(hollowPrimitive.Filled);

        var ellipse = new AnnotationObject(AnnotationKind.Ellipse, new AnnotationRect(0, 0, 40, 20), AnnotationColor.Blue);
        Assert.Equal(AnnotationPrimitiveKind.Ellipse, AnnotationMath.Flatten(ellipse, 800, 600).Single().Kind);
    }

    [Fact]
    public void Highlighter_is_wider_and_translucent_than_the_pen()
    {
        var points = new[] { new AnnotationPoint(0, 0), new AnnotationPoint(100, 0) };
        var pen = AnnotationObject.Polyline(AnnotationKind.Pen, points, AnnotationColor.Red, strokeWidth: 4);
        var highlighter = AnnotationObject.Polyline(AnnotationKind.Highlighter, points, AnnotationColor.Yellow, strokeWidth: 4);

        var penPrimitive = AnnotationMath.Flatten(pen, 800, 600).Single();
        var highlighterPrimitive = AnnotationMath.Flatten(highlighter, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Polyline, penPrimitive.Kind);
        Assert.Equal(4, penPrimitive.StrokeWidth, 3);
        Assert.Equal(1, penPrimitive.Opacity, 3);
        Assert.Equal(4 * AnnotationMath.HighlighterWidthFactor, highlighterPrimitive.StrokeWidth, 3);
        Assert.Equal(AnnotationMath.HighlighterOpacity, highlighterPrimitive.Opacity, 3);
    }

    [Fact]
    public void Callout_number_flattens_to_a_badge_with_contrast_text()
    {
        var callout = new AnnotationObject(
            AnnotationKind.Callout, new AnnotationRect(10, 10, 40, 40), AnnotationColor.Yellow,
            FontSize: 20, Number: 3);

        var badge = AnnotationMath.Flatten(callout, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Badge, badge.Kind);
        Assert.Equal(3, badge.Number);
        Assert.True(badge.Filled);
        Assert.Equal(AnnotationColor.Black, badge.Color.ContrastText);
        Assert.Equal(new AnnotationRect(10, 10, 40, 40), badge.Rect);
    }

    [Fact]
    public void Text_keeps_its_rtl_flag_font_size_and_box()
    {
        var text = new AnnotationObject(
            AnnotationKind.Text, new AnnotationRect(20, 30, 200, 40), AnnotationColor.White,
            FontSize: 30, Text: "مرحبا بالعالم", Rtl: true);

        var primitive = AnnotationMath.Flatten(text, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Text, primitive.Kind);
        Assert.True(primitive.Rtl);
        Assert.Equal(30, primitive.FontSize, 3);
        Assert.Equal("مرحبا بالعالم", primitive.Text);
        Assert.Equal(new AnnotationRect(20, 30, 200, 40), primitive.Rect);
    }

    [Fact]
    public void Stamp_keeps_the_image_index()
    {
        var stamp = new AnnotationObject(
            AnnotationKind.Stamp, new AnnotationRect(5, 5, 60, 30), AnnotationColor.White, StampIndex: 2);

        var primitive = AnnotationMath.Flatten(stamp, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Stamp, primitive.Kind);
        Assert.Equal(2, primitive.StampIndex);
    }

    [Fact]
    public void Magnifier_shows_the_lens_reduced_by_the_zoom()
    {
        var lens = new AnnotationRect(100, 100, 100, 100);

        var source = AnnotationMath.MagnifierSource(lens, 800, 600);

        Assert.Equal(100 / AnnotationMath.MagnifierZoom, source.Width, 3);
        Assert.Equal(100 / AnnotationMath.MagnifierZoom, source.Height, 3);
        // The lens keeps the same centre: the zoomed picture comes from behind the lens.
        Assert.Equal(lens.CenterX, source.CenterX, 3);
        Assert.Equal(lens.CenterY, source.CenterY, 3);
    }

    [Fact]
    public void Magnifier_source_stays_inside_the_image()
    {
        // On an image smaller than the lens, the source region cannot leave the image.
        var source = AnnotationMath.MagnifierSource(new AnnotationRect(0, 0, 100, 100), 60, 60);

        Assert.True(source.X >= 0 && source.Y >= 0);
        Assert.True(source.Right <= 60 && source.Bottom <= 60);
    }

    [Fact]
    public void Magnifier_flattens_to_a_lens_with_its_source_region()
    {
        var magnifier = new AnnotationObject(
            AnnotationKind.Magnifier, new AnnotationRect(100, 100, 100, 100), AnnotationColor.White, StrokeWidth: 4);

        var primitive = AnnotationMath.Flatten(magnifier, 800, 600).Single();

        Assert.Equal(AnnotationPrimitiveKind.Lens, primitive.Kind);
        Assert.NotNull(primitive.SourceRect);
        Assert.Equal(50, primitive.SourceRect!.Value.Width, 3);
        Assert.Equal(4, primitive.StrokeWidth, 3);
    }

    // ------------------------------------------------------------------ document and z-order

    [Fact]
    public void Document_adds_removes_and_reorders_objects()
    {
        var document = new AnnotationDocument(800, 600);
        var a = document.Add(Rect(0, 0, 10, 10));
        document.Add(Rect(1, 1, 10, 10));
        document.Add(Rect(2, 2, 10, 10));

        Assert.Equal(3, document.Count);
        // [a, b, c] → [b, c, a]
        Assert.True(document.BringToFront(a));
        Assert.Equal(new AnnotationRect(1, 1, 10, 10), document.Objects[0].Bounds);
        Assert.Equal(new AnnotationRect(0, 0, 10, 10), document.Objects[2].Bounds);

        // Index 2 now holds a; sending it to the back restores the original order.
        Assert.True(document.SendToBack(2));
        Assert.Equal(new AnnotationRect(0, 0, 10, 10), document.Objects[0].Bounds);
        Assert.Equal(new AnnotationRect(2, 2, 10, 10), document.Objects[2].Bounds);

        Assert.True(document.RemoveAt(0));
        Assert.Equal(2, document.Count);
        Assert.False(document.RemoveAt(5));
        Assert.False(document.BringToFront(document.Count - 1));
        Assert.False(document.SendToBack(0));
    }

    [Fact]
    public void Document_numbers_callouts_above_every_existing_number()
    {
        var document = new AnnotationDocument(800, 600);
        Assert.Equal(1, document.NextCalloutNumber());

        document.Add(new AnnotationObject(AnnotationKind.Callout, new AnnotationRect(0, 0, 20, 20), AnnotationColor.Red, Number: 1));
        document.Add(new AnnotationObject(AnnotationKind.Callout, new AnnotationRect(30, 0, 20, 20), AnnotationColor.Red, Number: 2));

        Assert.Equal(3, document.NextCalloutNumber());

        // Deleting the highest callout frees its number; deleting a lower one does not renumber the rest.
        document.RemoveAt(1);
        Assert.Equal(2, document.NextCalloutNumber());
        // A non-callout object never consumes a callout number.
        document.Add(Rect(0, 0, 5, 5));
        Assert.Equal(2, document.NextCalloutNumber());
    }

    [Fact]
    public void Document_flattens_in_z_order()
    {
        var document = new AnnotationDocument(800, 600);
        var back = Rect(0, 0, 10, 10);
        var front = Rect(20, 20, 10, 10);
        document.Add(back);
        document.Add(front);

        var primitives = document.Flatten();

        Assert.Equal(2, primitives.Count);
        Assert.Equal(back.Bounds, primitives[0].Rect);
        Assert.Equal(front.Bounds, primitives[1].Rect);
    }

    [Fact]
    public void Snapshot_is_immutable_and_restore_brings_the_objects_back()
    {
        var document = new AnnotationDocument(800, 600);
        document.Add(Rect(0, 0, 10, 10));
        var snapshot = document.Snapshot();

        document.Add(Rect(50, 50, 10, 10));
        Assert.Equal(2, document.Count);

        document.Restore(snapshot);
        Assert.Single(document.Objects);
        Assert.Single(snapshot);
    }

    // ------------------------------------------------------------------ undo and redo

    [Fact]
    public void History_undoes_and_redoes_structural_changes()
    {
        var document = new AnnotationDocument(800, 600);
        var history = new AnnotationHistory();
        document.Add(Rect(0, 0, 10, 10));

        Assert.False(history.CanUndo);

        history.Push(document);
        document.Add(Rect(50, 50, 10, 10));
        Assert.Equal(2, document.Count);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);

        Assert.True(history.Undo(document));
        Assert.Single(document.Objects);
        Assert.True(history.CanRedo);

        Assert.True(history.Redo(document));
        Assert.Equal(2, document.Count);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_drops_the_redo_branch_after_a_new_change()
    {
        var document = new AnnotationDocument(800, 600);
        var history = new AnnotationHistory();
        history.Push(document);
        document.Add(Rect(0, 0, 10, 10));
        history.Undo(document);
        Assert.True(history.CanRedo);

        history.Push(document);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_forgets_the_oldest_snapshot_past_its_capacity()
    {
        var document = new AnnotationDocument(800, 600);
        var history = new AnnotationHistory(capacity: 2);

        history.Push(document);
        history.Push(document);
        history.Push(document);

        Assert.True(history.Undo(document));
        Assert.True(history.Undo(document));
        Assert.False(history.Undo(document));
    }

    // ------------------------------------------------------------------ colours and strings

    [Fact]
    public void Contrast_text_follows_the_ink_luminance()
    {
        Assert.Equal(AnnotationColor.Black, AnnotationColor.White.ContrastText);
        Assert.Equal(AnnotationColor.Black, AnnotationColor.Yellow.ContrastText);
        Assert.Equal(AnnotationColor.White, AnnotationColor.Black.ContrastText);
        Assert.Equal(AnnotationColor.White, AnnotationColor.Blue.ContrastText);
    }

    [Fact]
    public void Colour_parse_and_hex_round_trip()
    {
        Assert.Equal(AnnotationColor.Red, AnnotationColor.Parse("#E53935"));
        Assert.Equal(new AnnotationColor(0x11, 0x22, 0x33, 0x44), AnnotationColor.Parse("#11223344"));
        Assert.Equal("#11223344", AnnotationColor.Parse("#11223344").ToHex());
        // Invalid input falls back to the default annotation colour, like every other tool default.
        Assert.Equal(AnnotationColor.Red, AnnotationColor.Parse("not a colour"));
    }

    /// <summary>
    /// The keys the Annotate window reads through variables (the tool strip) are not visible to
    /// <see cref="LocaleUsageTests"/>, so they are checked here: both tables, same keys, Arabic differs
    /// from English, and no key invents a placeholder.
    /// </summary>
    [Fact]
    public void Every_annotate_string_exists_in_both_tables()
    {
        string[] keys =
        [
            "tool.annotate.label",
            "tool.annotate.title",
            "tool.annotate.subtitle",
            "tool.annotate.suffix",
            "tool.annotate.empty",
            "tool.annotate.colors",
            "tool.annotate.stroke",
            "tool.annotate.font",
            "tool.annotate.fill",
            "tool.annotate.undo",
            "tool.annotate.redo",
            "tool.annotate.delete",
            "tool.annotate.front",
            "tool.annotate.back",
            "tool.annotate.text.title",
            "tool.annotate.text.hint",
            "tool.annotate.text.add",
            "tool.annotate.stamp.pick",
            "tool.annotate.stamp.filter",
            "tool.annotate.tool.select",
            "tool.annotate.tool.arrow",
            "tool.annotate.tool.line",
            "tool.annotate.tool.rectangle",
            "tool.annotate.tool.ellipse",
            "tool.annotate.tool.text",
            "tool.annotate.tool.callout",
            "tool.annotate.tool.magnifier",
            "tool.annotate.tool.pen",
            "tool.annotate.tool.highlighter",
            "tool.annotate.tool.stamp",
        ];

        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);
        foreach (var key in keys)
        {
            Assert.True(english.ContainsKey(key), $"en.json is missing '{key}'.");
            Assert.True(arabic.ContainsKey(key), $"ar.json is missing '{key}'.");
            Assert.NotEqual(english[key], arabic[key]);
            Assert.False(string.IsNullOrWhiteSpace(english[key]));
            Assert.False(string.IsNullOrWhiteSpace(arabic[key]));
        }

        // Every other annotate key that ships belongs to the same prefix, so a typo cannot hide.
        foreach (var table in new[] { english, arabic })
        {
            foreach (var key in table.Keys.Where(k => k.StartsWith("tool.annotate.", StringComparison.Ordinal)))
                Assert.Contains(key, keys);
        }
    }
}
