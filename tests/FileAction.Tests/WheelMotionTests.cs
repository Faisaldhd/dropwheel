using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using FileAction.App;
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
    public void Dropping_a_child_runs_it_while_a_category_parent_still_opens_the_chooser()
    {
        var child = new FakeOperation("child");
        var group = Group("category.test", child);
        Operation[] outer = [group, new FakeOperation("direct")];
        var choices = WheelWindow.InnerRing(group.Children, 0);

        // A drop on the category parent keeps opening the category, not running it as a job.
        var parent = WheelWindow.DropTargetAt(WheelZone.Outer, 0, outer, choices);
        Assert.Equal(WheelWindow.DropAction.OpenCategory, parent.Action);
        Assert.Same(group, parent.Operation);

        // A plain outer slice is run directly.
        var direct = WheelWindow.DropTargetAt(WheelZone.Outer, 1, outer, choices);
        Assert.Equal(WheelWindow.DropAction.Run, direct.Action);
        Assert.Same(outer[1], direct.Operation);

        // An inner child dispatches its own tool.
        var inner = WheelWindow.DropTargetAt(WheelZone.Inner, 0, outer, choices);
        Assert.Equal(WheelWindow.DropAction.Run, inner.Action);
        Assert.Same(child, inner.Operation);
    }

    [Fact]
    public void Dropping_outside_the_disc_or_on_a_pager_cancels()
    {
        var child = new FakeOperation("child");
        var group = Group("category.test", child);
        Operation[] outer = [group];
        var choices = WheelWindow.InnerRing(group.Children, 0);

        foreach (var zone in new[] { WheelZone.None, WheelZone.Hub })
        {
            var target = WheelWindow.DropTargetAt(zone, 0, outer, choices);
            Assert.Equal(WheelWindow.DropAction.Nothing, target.Action);
            Assert.Null(target.Operation);
        }

        // An out-of-range index and the compact "+n" slice are not tools: a drop on either cancels.
        Assert.Equal(WheelWindow.DropAction.Nothing, WheelWindow.DropTargetAt(WheelZone.Outer, 99, outer, choices).Action);
        var more = new InnerChoice(null, 4);
        Assert.Equal(WheelWindow.DropAction.Nothing, WheelWindow.DropTargetAt(WheelZone.Inner, 0, outer, [more]).Action);
    }

    [Fact]
    public void Drag_mode_dwells_for_450_ms_paints_the_arc_and_then_opens_the_category()
    {
        RunOnSta(() =>
        {
            var child = new FakeOperation("child");
            var group = Group("category.test", child);
            Operation[] outer = [group, new FakeOperation("direct")];
            var window = new WheelWindow(
                WheelMode.Drag,
                WheelKind.Tools,
                [new FileItem("photo.png")],
                new AppSettings(),
                (_, _) => outer,
                (_, _, _) => { });
            try
            {
                var size = (double)typeof(WheelWindow)
                    .GetField("Size", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetRawConstantValue()!;
                var categoryPoint = new Point(size / 2, size / 2 - WheelWindow.OuterLabelRadius);

                // Hovering the category starts the dwell and paints its progress arc.
                Invoke(window, "PointerMove", categoryPoint);
                Assert.Equal(0, Field<int>(window, "_dwellIndex"));
                Assert.NotNull(Field<object?>(window, "_dwellArc"));
                var timer = Field<DispatcherTimer?>(window, "_dwellTimer");
                Assert.NotNull(timer);
                Assert.True(timer!.IsEnabled);

                // Run the tick the timer would run after 450 ms; this avoids a wall-clock sleep.
                SetField(window, "_dwellStarted", Environment.TickCount64 - (long)WheelWindow.DwellMs - 1);
                Invoke(window, "DwellTick");

                Assert.Equal(-1, Field<int>(window, "_dwellIndex"));
                Assert.Null(Field<object?>(window, "_dwellArc"));
                Assert.False(timer.IsEnabled);
                Assert.Equal(group.GroupId, Field<string?>(window, "_innerGroupId"));
                Assert.Single(Field<IReadOnlyList<InnerChoice>>(window, "_innerChoices"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Moving_from_a_category_straight_to_the_hub_opens_its_ring_immediately()
    {
        RunOnSta(() =>
        {
            var child = new FakeOperation("child");
            var group = Group("category.test", child);
            Operation[] outer = [group, new FakeOperation("direct")];
            var window = new WheelWindow(
                WheelMode.Drag,
                WheelKind.Tools,
                [new FileItem("photo.png")],
                new AppSettings(),
                (_, _) => outer,
                (_, _, _) => { });
            try
            {
                var size = (double)typeof(WheelWindow)
                    .GetField("Size", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetRawConstantValue()!;
                var category = new Point(size / 2, size / 2 - WheelWindow.OuterLabelRadius);
                var hub = new Point(size / 2, size / 2);

                Invoke(window, "PointerMove", category);
                Assert.Equal(0, Field<int>(window, "_dwellIndex"));

                Invoke(window, "PointerMove", hub);

                Assert.Equal(-1, Field<int>(window, "_dwellIndex"));
                Assert.Equal(group.GroupId, Field<string?>(window, "_innerGroupId"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void A_pending_swap_commits_when_the_pointer_heads_inward()
    {
        RunOnSta(() =>
        {
            var a = Group("category.a", new FakeOperation("a-child"));
            var b = Group("category.b", new FakeOperation("b-child"));
            Operation[] outer = [a, b];
            var window = new WheelWindow(
                WheelMode.Drag,
                WheelKind.Tools,
                [new FileItem("photo.png")],
                new AppSettings(),
                (_, _) => outer,
                (_, _, _) => { });
            try
            {
                var size = (double)typeof(WheelWindow)
                    .GetField("Size", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetRawConstantValue()!;
                var top = new Point(size / 2, size / 2 - WheelWindow.OuterLabelRadius);
                var bottom = new Point(size / 2, size / 2 + WheelWindow.OuterLabelRadius);
                var hub = new Point(size / 2, size / 2);

                // Open A through its dwell, then hover B and head for the hub before the swap timer.
                Invoke(window, "PointerMove", top);
                SetField(window, "_dwellStarted", Environment.TickCount64 - (long)WheelWindow.DwellMs - 1);
                Invoke(window, "DwellTick");
                Assert.Equal(a.GroupId, Field<string?>(window, "_innerGroupId"));

                SetField(window, "_innerHighlight", 0);
                Invoke(window, "PointerMove", bottom);
                Assert.Equal(1, Field<int>(window, "_swapIndex"));
                // Grazing the outer band must not clear the selected child before the swap commits.
                Assert.Equal(0, Field<int>(window, "_innerHighlight"));

                Invoke(window, "PointerMove", hub);

                Assert.Equal(b.GroupId, Field<string?>(window, "_innerGroupId"));
                Assert.Equal(-1, Field<int>(window, "_swapIndex"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void The_exact_centre_keeps_the_current_child()
    {
        Assert.Equal(2, WheelWindow.HubIndex(2, 5, 0, 0));
        Assert.Equal(2, WheelWindow.HubIndex(2, 5, 180, 50));
        Assert.Equal(0, WheelWindow.HubIndex(-1, 5, 0, 0));
        Assert.Equal(2, WheelWindow.HubIndex(0, 5, WheelWindow.SliceAngle(2, 5), 120));
    }

    [Fact]
    public void Hovering_the_centre_keeps_the_inner_highlight()
    {
        RunOnSta(() =>
        {
            var a = new FakeOperation("a");
            var b = new FakeOperation("b");
            var group = new WheelGroupOperation(
                new WheelGroupDefinition("category.test", "a", ["a", "b"]), a, [a, b]);
            Operation[] outer = [group];
            var window = new WheelWindow(
                WheelMode.Click,
                WheelKind.Tools,
                [new FileItem("photo.png")],
                new AppSettings(),
                (_, _) => outer,
                (_, _, _) => { });
            try
            {
                var size = (double)typeof(WheelWindow)
                    .GetField("Size", BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetRawConstantValue()!;
                Invoke(window, "PointerMove", new Point(size / 2, size / 2 - WheelWindow.OuterLabelRadius));
                SetField(window, "_dwellStarted", Environment.TickCount64 - (long)WheelWindow.DwellMs - 1);
                Invoke(window, "DwellTick");
                SetField(window, "_innerHighlight", 1);

                Invoke(window, "PointerMove", new Point(size / 2, size / 2));

                Assert.Equal(1, Field<int>(window, "_innerHighlight"));
            }
            finally
            {
                window.Close();
            }
        });
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

    private static WheelGroupOperation Group(string id, Operation child) =>
        new(new WheelGroupDefinition(id, child.Id, [child.Id]), child, [child]);

    private static void Invoke(WheelWindow window, string method, params object?[] args) =>
        typeof(WheelWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, args);

    private static T Field<T>(WheelWindow window, string name) =>
        (T)typeof(WheelWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window)!;

    private static void SetField(WheelWindow window, string name, object value) =>
        typeof(WheelWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, value);

    /// <summary>Runs a WPF window test on its own STA thread and rethrows the original failure.</summary>
    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakeOperation(string id) : Operation(id, WheelKind.Tools, id, "x")
    {
        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));
    }
}
