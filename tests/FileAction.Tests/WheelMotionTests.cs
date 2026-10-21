using FileAction.App.Wheel;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The 2.4.5 two-ring navigation rules, kept data-only so they run without a window: the outer ring is
/// never replaced by a category, hover waits for the dwell, Escape clears the inner ring before the
/// wheel can close, and every inner transition stays inside its motion budget.
/// </summary>
public class WheelMotionTests
{
    [Fact]
    public void Main_wheel_reveals_slices_in_authored_order()
    {
        Assert.Equal([0, 1, 2, 3], WheelWindow.RevealOrder(4, null));
    }

    [Fact]
    public void Inner_ring_reveal_starts_at_the_category_slice_and_wraps_clockwise()
    {
        // Four children at 0/90/180/270 degrees; the category sits at 90, so its nearest child opens first.
        Assert.Equal([1, 2, 3, 0], WheelWindow.RevealOrder(4, 90));
        Assert.Equal(90, WheelWindow.SliceAngle(1, 4));
    }

    [Fact]
    public void Escape_clears_the_inner_ring_and_only_the_second_press_closes_the_wheel()
    {
        Assert.Equal(WheelEscape.ClearInner, WheelWindow.EscapeActionFor(innerOpen: true, dwelling: false));
        Assert.Equal(WheelEscape.Close, WheelWindow.EscapeActionFor(innerOpen: false, dwelling: false));
        // An unfinished dwell is cancelled by the first Escape too, so it cannot open behind the close.
        Assert.Equal(WheelEscape.CancelDwell, WheelWindow.EscapeActionFor(innerOpen: false, dwelling: true));
    }

    [Fact]
    public void A_category_shows_its_children_without_replacing_the_outer_ring()
    {
        var first = new FakeOperation("a");
        var second = new FakeOperation("b");
        var group = new WheelGroupOperation(
            new WheelGroupDefinition("category.transform", "a", ["a", "b"]), first, [first, second]);
        Operation[] root = [group, new FakeOperation("extract"), new FakeOperation("other")];

        var children = WheelWindow.ChildrenOf(root, "category.transform", out var found);
        Assert.True(found);
        Assert.Equal([first, second], children);

        // No view swap: the whole outer ring is projected back when no category is asked for, and an
        // unknown category leaves that same ring on screen instead of a fallback list of children.
        var same = WheelWindow.ChildrenOf(root, null, out found);
        Assert.True(found);
        Assert.Same(root, same);

        var fallback = WheelWindow.ChildrenOf(root, "category.missing", out found);
        Assert.False(found);
        Assert.Equal(root, fallback);
    }

    [Fact]
    public void Hover_waits_for_the_dwell_and_swaps_only_once_a_ring_is_open()
    {
        Assert.True(WheelWindow.ShouldDwell(innerOpen: false, sameCategory: false, suppressed: false));
        Assert.False(WheelWindow.ShouldDwell(innerOpen: false, sameCategory: false, suppressed: true));
        Assert.False(WheelWindow.ShouldDwell(innerOpen: true, sameCategory: false, suppressed: false));
        Assert.True(WheelWindow.ShouldSwap(innerOpen: true, sameCategory: false));
        Assert.False(WheelWindow.ShouldSwap(innerOpen: true, sameCategory: true));
        Assert.False(WheelWindow.ShouldSwap(innerOpen: false, sameCategory: false));
    }

    [Fact]
    public void The_inner_ring_never_waits_visibly_or_exceeds_its_motion_budget()
    {
        // Owner's budget: a short 450 ms dwell with a visible progress arc, entrance <= 120 ms,
        // category swap <= 100 ms, clear on Escape <= 100 ms. The first entrance frame is already
        // visible (0.4).
        Assert.Equal(450, WheelWindow.DwellMs);
        Assert.True(WheelWindow.InnerEnterMs + (WheelWindow.InnerCapacity - 1) * WheelWindow.InnerEnterStaggerMs <= 120);
        Assert.True(WheelWindow.InnerSwapOutMs + WheelWindow.InnerSwapInMs <= 100);
        Assert.True(WheelWindow.InnerExitMs <= 100);
        Assert.InRange(WheelWindow.InnerRevealFrom, 0.3, 0.8);
        Assert.InRange(WheelWindow.InnerScaleFrom, 0.8, 0.99);
    }

    private sealed class FakeOperation(string id) : Operation(id, WheelKind.Tools, id, "x")
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
