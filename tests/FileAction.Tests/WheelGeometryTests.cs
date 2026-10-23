using FileAction.App;
using FileAction.App.Wheel;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The two-ring geometry, checked without a window: the hub and the two rings are disjoint bands, every
/// label box is derived from the chord of its slice at the narrowest radius it occupies (so the clip can
/// never cut resting text), and a category with more than six children pages with a compact "+n" slice
/// instead of crowding the inner ring. The last test pins the seven intent categories of an image
/// selection, because that is the outer ring the owner's sketch always shows.
/// </summary>
public class WheelGeometryTests
{
    [Fact]
    public void The_hub_and_the_two_rings_are_disjoint_and_ordered()
    {
        Assert.True(WheelWindow.HubRadius < WheelWindow.InnerInnerRadius);
        Assert.True(WheelWindow.InnerInnerRadius < WheelWindow.InnerOuterRadius);
        Assert.True(WheelWindow.InnerOuterRadius + 6 <= WheelWindow.OuterInnerRadius);
        Assert.True(WheelWindow.OuterInnerRadius < WheelWindow.OuterRadius);

        // Labels stay inside their own ring: the box never crosses an edge.
        Assert.True(WheelWindow.OuterLabelRadius - WheelWindow.OuterLabelMaxHeight / 2 > WheelWindow.OuterInnerRadius);
        Assert.True(WheelWindow.OuterLabelRadius + WheelWindow.OuterLabelMaxHeight / 2 < WheelWindow.OuterRadius);
        Assert.True(WheelWindow.InnerLabelRadius - WheelWindow.InnerLabelMaxHeight / 2 > WheelWindow.InnerInnerRadius);
        Assert.True(WheelWindow.InnerLabelRadius + WheelWindow.InnerLabelMaxHeight / 2 < WheelWindow.InnerOuterRadius);

        // The inner ring's type is genuinely smaller than the outer ring's.
        Assert.True(WheelWindow.InnerLabelMaxHeight < WheelWindow.OuterLabelMaxHeight);
    }

    [Fact]
    public void Radius_zones_map_to_hub_inner_outer_and_nothing_outside()
    {
        Assert.Equal(WheelZone.Hub, WheelWindow.ZoneForRadius(0));
        Assert.Equal(WheelZone.Hub, WheelWindow.ZoneForRadius(WheelWindow.HubRadius - 1));
        Assert.Equal(WheelZone.Inner, WheelWindow.ZoneForRadius(WheelWindow.InnerInnerRadius));
        Assert.Equal(WheelZone.Inner, WheelWindow.ZoneForRadius(WheelWindow.InnerOuterRadius));
        Assert.Equal(WheelZone.Outer, WheelWindow.ZoneForRadius(WheelWindow.OuterInnerRadius));
        Assert.Equal(WheelZone.Outer, WheelWindow.ZoneForRadius(WheelWindow.OuterRadius + WheelWindow.Rim - 0.5));
        Assert.Equal(WheelZone.None, WheelWindow.ZoneForRadius(WheelWindow.OuterRadius + WheelWindow.Rim + 0.5));

        // The inner ring belongs to the whole disc: the hub, the inner band and both outer-band edges
        // keep it. Only a point past the rim is outside and may dismiss it.
        Assert.True(WheelWindow.KeepsInnerRing(WheelZone.Hub));
        Assert.True(WheelWindow.KeepsInnerRing(WheelZone.Inner));
        Assert.True(WheelWindow.KeepsInnerRing(WheelZone.Outer));
        Assert.False(WheelWindow.KeepsInnerRing(WheelZone.None));
    }

    [Fact]
    public void Slice_hit_testing_wraps_around_the_ring()
    {
        Assert.Equal(0, WheelWindow.SliceIndexAt(0, 4));
        Assert.Equal(1, WheelWindow.SliceIndexAt(90, 4));
        // Slice 3 spans 225°..315°; 314° is still in it and the next degree wraps to slice 0.
        Assert.Equal(3, WheelWindow.SliceIndexAt(314, 4));
        Assert.Equal(0, WheelWindow.SliceIndexAt(316, 4));
        Assert.Equal(-1, WheelWindow.SliceIndexAt(0, 0));
    }

    [Fact]
    public void Outer_label_boxes_fit_inside_their_slice_at_every_slice_count()
    {
        for (var count = 2; count <= 19; count++)
        {
            var width = WheelWindow.LabelWidth(count, WheelWindow.OuterLabelRadius, WheelWindow.OuterLabelMaxHeight);
            var chord = WheelWindow.ChordAt(count, WheelWindow.OuterLabelRadius - WheelWindow.OuterLabelMaxHeight / 2);
            Assert.True(width < chord, $"{count} slices: label {width:0.0} wider than chord {chord:0.0}");
        }
    }

    [Fact]
    public void Inner_label_boxes_fit_the_worst_case_six_slice_ring()
    {
        var chord = WheelWindow.ChordAt(
            WheelWindow.InnerCapacity,
            WheelWindow.InnerLabelRadius - WheelWindow.InnerLabelMaxHeight / 2);
        Assert.True(WheelWindow.InnerLabelWidth < chord);

        for (var count = 2; count <= WheelWindow.InnerCapacity; count++)
            for (var index = 0; index < count; index++)
            {
                var width = WheelWindow.InnerSliceLabelWidth(count, index);
                Assert.InRange(width, 20, WheelWindow.InnerLabelWidth);
                Assert.True(WheelWindow.LabelBoxFits(
                    count, index, WheelWindow.InnerInnerRadius, WheelWindow.InnerOuterRadius,
                    WheelWindow.InnerLabelRadius, width, WheelWindow.InnerLabelMaxHeight),
                    $"{count} inner slices, slice {index}: width {width:0.0} leaves the band");
            }

        // A left/right slice only has the ring's radial thickness as horizontal room, so its box is
        // narrower there than at the top; the side of a six-slice ring is the tightest case.
        Assert.True(WheelWindow.InnerSliceLabelWidth(6, 1) < WheelWindow.InnerSliceLabelWidth(6, 0));
        Assert.True(WheelWindow.InnerSliceLabelWidth(6, 1) < WheelWindow.InnerLabelWidth);
        Assert.Equal(8, WheelWindow.InnerCapacity);
        Assert.Equal(7, WheelWindow.InnerPerPage);
    }

    [Fact]
    public void Inner_labels_use_the_format_subtitle_and_the_short_form()
    {
        // pack.zip and pack.tar.gz share the "Archive" label; the subtitle tells the formats apart.
        Assert.Equal("ZIP", WheelWindow.InnerLabelText(new FakeOperation("pack.zip", "Archive", "ZIP")));
        Assert.Equal("TAR.GZ", WheelWindow.InnerLabelText(new FakeOperation("pack.tar.gz", "Archive", "TAR.GZ")));
        // Long labels are shortened so the single line fits the inner band; the TextBlock ellipsizes the
        // rest, but these two were explicitly unreadable before.
        Assert.Equal("Metadata", WheelWindow.InnerLabelText(new FakeOperation("tools.metadata", "Edit metadata", "Tags and chapters")));
        Assert.Equal("1920 px", WheelWindow.InnerLabelText(new FakeOperation("image.max1920", "Max 1920 px", "for web")));
        Assert.Equal("QR code", WheelWindow.InnerLabelText(new FakeOperation("tools.qr", "Read QR code", "from an image")));
        Assert.Equal("Rotate R", WheelWindow.InnerLabelText(new FakeOperation("image.rotate.right", "Rotate right", "clockwise")));
        Assert.Equal("Rotate L", WheelWindow.InnerLabelText(new FakeOperation("image.rotate.left", "Rotate left", "counter-clockwise")));
        // An ordinary short label is unchanged.
        Assert.Equal("Compress", WheelWindow.InnerLabelText(new FakeOperation("tools.compress", "Compress", "smaller file")));
    }

    [Fact]
    public void Hub_badge_names_the_selection_format_not_the_wheel_category()
    {
        var root = Path.GetTempPath();
        Assert.Equal("FILES", WheelWindow.FormatBadgeText([]));
        Assert.Equal("JPG", WheelWindow.FormatBadgeText([new FileItem(Path.Combine(root, "photo.JPEG"))]));
        Assert.Equal("PNG", WheelWindow.FormatBadgeText([new FileItem(Path.Combine(root, "shot.png"))]));
        Assert.Equal("PDF", WheelWindow.FormatBadgeText([new FileItem(Path.Combine(root, "paper.pdf"))]));
        Assert.Equal("MP4", WheelWindow.FormatBadgeText([new FileItem(Path.Combine(root, "clip.m4v"))]));
        Assert.Equal("TAR.GZ", WheelWindow.FormatBadgeText([new FileItem(Path.Combine(root, "bundle.tar.gz"))]));
        Assert.Equal("DIR", WheelWindow.FormatBadgeText([new FileItem(root)]));
        Assert.Equal("FILES", WheelWindow.FormatBadgeText([
            new FileItem(Path.Combine(root, "one.jpg")),
            new FileItem(Path.Combine(root, "two.pdf")),
        ]));
    }

    [Fact]
    public void A_category_with_more_than_eight_children_pages_instead_of_crowding()
    {
        var children = Enumerable.Range(0, 12).Select(i => (Operation)new FakeOperation($"op{i}")).ToArray();
        Assert.Equal(1, WheelWindow.InnerPageCount(8));
        Assert.Equal(2, WheelWindow.InnerPageCount(9));
        Assert.Equal(2, WheelWindow.InnerPageCount(12));

        var first = WheelWindow.InnerRing(children, 0);
        Assert.Equal(8, first.Count);
        Assert.Equal(7, first.Count(choice => !choice.IsMore));
        Assert.True(first[^1].IsMore);
        Assert.Equal(5, first[^1].Hidden);
        Assert.Equal(children.Take(7).Select(child => child.Id), first.Where(c => !c.IsMore).Select(c => c.Operation!.Id));

        // Every page stays within the ring capacity and always offers the way to the next page.
        for (var page = 0; page < WheelWindow.InnerPageCount(children.Length); page++)
        {
            var ring = WheelWindow.InnerRing(children, page);
            Assert.InRange(ring.Count, 1, WheelWindow.InnerCapacity);
            Assert.Contains(ring, choice => choice.IsMore);
        }

        // Eight or fewer fit as they are: no "+n" slice is added while there is room.
        var eight = WheelWindow.InnerRing(children.Take(8).ToArray(), 0);
        Assert.Equal(8, eight.Count);
        Assert.DoesNotContain(eight, choice => choice.IsMore);
    }

    [Fact]
    public void The_six_intent_categories_of_an_image_stay_on_the_outer_ring()
    {
        // A real image in the repository, so the Lock operation's "the file exists" rule applies and the
        // Protect category forms exactly as it does for an owner's photo.
        var file = Path.Combine(RepoRoot(), "src", "FileAction.App", "Assets", "DropWheel.png");
        Assert.True(File.Exists(file), $"missing sample image: {file}");

        var slices = WheelController.Offered(
            WheelKind.Tools,
            [new FileItem(file)],
            new ToolLocator(Path.GetTempPath()),
            new AppSettings(),
            WheelController.BuildOperations());

        Assert.Equal(
            ["Crop & rotate", "Optimize", "Annotate", "Metadata", "Protect", "Archive"],
            slices.Select(slice => slice.Label).ToArray());
        // Every slice opens a ring; Extract text now lives in Annotate, so there is no Other for an image.
        Assert.Equal(6, slices.OfType<WheelGroupOperation>().Count());
        Assert.Equal("wheel.group.category.archive", slices[^1].Id);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("FileAction.sln not found above the test output");
    }

    [Fact]
    public void Inner_hover_is_sticky_past_the_slice_midpoint()
    {
        // Five slices, 72 degrees each. Slice 0 is centered at 0.
        Assert.Equal(0, WheelWindow.InnerHoverIndex(30, 5, 0));  // before midpoint + 8
        Assert.Equal(0, WheelWindow.InnerHoverIndex(40, 5, 0));  // midpoint, still sticky
        Assert.Equal(1, WheelWindow.InnerHoverIndex(46, 5, 0));  // past midpoint + 8
        Assert.Equal(1, WheelWindow.InnerHoverIndex(80, 5, 1));
        Assert.Equal(4, WheelWindow.InnerHoverIndex(330, 5, 4)); // wraps across 0
    }

    private sealed class FakeOperation(string id, string? label = null, string? subtitle = null)
        : Operation(id, WheelKind.Tools, label ?? id, "x", subtitle)
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
