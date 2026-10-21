using FileAction.App.Wheel;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// The 2.4.3 wheel navigation rules, kept data-only so they run without a window: the reveal sweeps
/// clockwise (starting at the parent slice inside a category), and the category trail restores the
/// exact parent view instead of closing the wheel.
/// </summary>
public class WheelMotionTests
{
    [Fact]
    public void Main_wheel_reveals_slices_in_authored_order()
    {
        Assert.Equal([0, 1, 2, 3], WheelWindow.RevealOrder(4, null));
    }

    [Fact]
    public void Sub_wheel_reveal_starts_at_the_parent_slice_and_wraps_clockwise()
    {
        // Four slices at 0/90/180/270 degrees; the parent sits at 90, so it opens first, then 180, 270, 0.
        Assert.Equal([1, 2, 3, 0], WheelWindow.RevealOrder(4, 90));
        Assert.Equal(90, WheelWindow.SliceAngle(1, 4));
    }

    [Fact]
    public void Escaping_a_category_restores_the_parent_view_not_the_root()
    {
        var trail = new WheelTrail();
        Assert.False(trail.InSubWheel);

        trail.Enter(WheelKind.Tools, 2, false, "category.transform", 90);
        trail.Enter(WheelKind.Tools, 0, false, "category.optimize", 180);
        Assert.Equal(2, trail.Depth);
        Assert.Equal("category.optimize", trail.GroupId);

        // The first Escape restores the category that opened the nested one (its slice 0, angle 180).
        var firstBack = trail.Back();
        Assert.NotNull(firstBack);
        Assert.Equal("category.transform", trail.GroupId);
        Assert.Equal(0, firstBack!.Value.Highlight);
        Assert.Equal(180, firstBack.Value.OriginAngle);

        // The second Escape restores the main wheel; slice 2 is selected again.
        var secondBack = trail.Back();
        Assert.NotNull(secondBack);
        Assert.False(trail.InSubWheel);
        Assert.Null(trail.GroupId);
        Assert.Equal(2, secondBack!.Value.Highlight);
        Assert.Null(secondBack.Value.GroupId);
    }

    [Fact]
    public void A_category_projects_exactly_its_children_and_an_unknown_category_falls_back_to_the_root()
    {
        var first = new FakeOperation("a");
        var second = new FakeOperation("b");
        var group = new WheelGroupOperation(
            new WheelGroupDefinition("category.transform", "a", ["a", "b"]), first, [first, second]);
        Operation[] root = [group];

        var children = WheelWindow.Project(root, "category.transform", out var found);
        Assert.True(found);
        Assert.Equal([first, second], children);

        var fallback = WheelWindow.Project(root, "category.missing", out found);
        Assert.False(found);
        Assert.Equal(root, fallback);
    }

    private sealed class FakeOperation(string id) : Operation(id, WheelKind.Tools, id, "x")
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
