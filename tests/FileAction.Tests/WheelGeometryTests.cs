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
        Assert.Equal(6, WheelWindow.InnerCapacity);
        Assert.Equal(5, WheelWindow.InnerPerPage);
    }

    [Fact]
    public void A_category_with_more_than_six_children_pages_instead_of_crowding()
    {
        var children = Enumerable.Range(0, 12).Select(i => (Operation)new FakeOperation($"op{i}")).ToArray();
        Assert.Equal(1, WheelWindow.InnerPageCount(6));
        Assert.Equal(2, WheelWindow.InnerPageCount(7));
        Assert.Equal(3, WheelWindow.InnerPageCount(12));

        var first = WheelWindow.InnerRing(children, 0);
        Assert.Equal(6, first.Count);
        Assert.Equal(5, first.Count(choice => !choice.IsMore));
        Assert.True(first[^1].IsMore);
        Assert.Equal(7, first[^1].Hidden);
        Assert.Equal(children.Take(5).Select(child => child.Id), first.Where(c => !c.IsMore).Select(c => c.Operation!.Id));

        // Every page stays at six slices or fewer and always offers the way to the next page.
        for (var page = 0; page < WheelWindow.InnerPageCount(children.Length); page++)
        {
            var ring = WheelWindow.InnerRing(children, page);
            Assert.InRange(ring.Count, 1, WheelWindow.InnerCapacity);
            Assert.Contains(ring, choice => choice.IsMore);
        }

        // Six or fewer fit as they are: no "+n" slice is added while there is room.
        var six = WheelWindow.InnerRing(children.Take(6).ToArray(), 0);
        Assert.Equal(6, six.Count);
        Assert.DoesNotContain(six, choice => choice.IsMore);
    }

    [Fact]
    public void The_seven_intent_categories_of_an_image_stay_on_the_outer_ring()
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
            ["Crop & rotate", "Optimize", "Annotate", "Metadata", "Protect", "Archive", "Extract text"],
            slices.Select(slice => slice.Label).ToArray());
        // Extract text is a direct operation, not a category that replaces the outer ring.
        Assert.Equal(6, slices.OfType<WheelGroupOperation>().Count());
        Assert.Equal("tools.ocr", slices[^1].Id);
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

    private sealed class FakeOperation(string id) : Operation(id, WheelKind.Tools, id, "x")
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
