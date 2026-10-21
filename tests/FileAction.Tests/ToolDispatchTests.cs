using FileAction.App;
using FileAction.App.Jobs;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Guards the wheel → tool-window dispatch. A tool can be registered, tested and localized and still be
/// invisible if its operation is never added to <see cref="WheelController.BuildOperations"/>: that is
/// exactly what happened to <c>tools.annotate</c> and <c>tools.redact</c> in 2.4.0 (two dead windows,
/// fixed in the same commit as this test).
///
/// The match is by identity — the operation id equals the tool id, and the operation must be an
/// <see cref="IInteractiveTool"/> so the wheel opens the window instead of starting a job — never by
/// <c>ToolRegistry.For(items)</c>, which is first-match and ambiguous when two tools accept the same file.
///
/// Grouped parents are checked through <see cref="WheelGroups.Flatten"/>: folding Crop with Crop Square
/// may change the slice, never the set of reachable operation ids.
/// </summary>
public class ToolDispatchTests
{
    /// <summary>The slices the wheels actually render, after hiding, favourites and grouping.</summary>
    private static IReadOnlyList<Operation> WheelSlices() =>
        WheelGroups.Arrange(WheelLayout.Arrange(WheelController.BuildOperations(), [], []));

    [Fact]
    public void EveryRegisteredToolWindowIsReachableFromTheWheel()
    {
        var byId = WheelGroups.Flatten(WheelSlices())
            .GroupBy(operation => operation.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var missing = new List<string>();
        var notInteractive = new List<string>();

        foreach (var spec in ToolRegistry.All)
        {
            if (!byId.TryGetValue(spec.Id, out var matches))
            {
                missing.Add(spec.Id);
                continue;
            }

            if (!matches.Any(operation => operation is IInteractiveTool))
                notInteractive.Add(spec.Id);
        }

        Assert.True(missing.Count == 0,
            "these registered tool windows have no wheel operation, so they can never open: "
            + string.Join(", ", missing));

        Assert.True(notInteractive.Count == 0,
            "these wheel operations would run a background job instead of opening the tool window: "
            + string.Join(", ", notInteractive));
    }

    [Fact]
    public void WheelOperationIdsAreUnique()
    {
        var duplicates = WheelSlices()
            .GroupBy(operation => operation.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' x{group.Count()}")
            .ToArray();

        Assert.True(duplicates.Length == 0,
            "two wheel operations share an id (last-used-operation memory and the wheel editor key on it): "
            + string.Join(", ", duplicates));
    }

    [Fact]
    public void GroupingKeepsEveryOperationReachableExactlyOnce()
    {
        var operations = WheelLayout.Arrange(WheelController.BuildOperations(), [], []);
        var flattened = WheelGroups.Flatten(WheelGroups.Arrange(operations));

        Assert.Equal(
            operations.Select(operation => operation.Id).Order(StringComparer.Ordinal),
            flattened.Select(operation => operation.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BatchRotateResizeAndArchiveAreParentSlicesAndToolsStayDirect()
    {
        var slices = WheelSlices();
        var groups = slices.OfType<WheelGroupOperation>().ToArray();

        // Interactive tool windows must stay one click away: a tool hidden behind a chooser reads as
        // "the tool does not open". Crop and Crop Square are separate slices on purpose.
        Assert.DoesNotContain(groups, group => group.GroupId == "image.crop");
        Assert.Contains(slices, slice => slice.Id == "tools.crop");
        Assert.Contains(slices, slice => slice.Id == "image.square");

        var rotate = Assert.Single(groups, group => group.GroupId == "image.rotate");
        Assert.Equal("Rotate", rotate.Label);
        Assert.Equal(
            new[] { "image.rotate.right", "image.rotate.left" },
            rotate.Children.Select(child => child.Id).ToArray());

        var resize = Assert.Single(groups, group => group.GroupId == "image.resize");
        Assert.Equal("Resize", resize.Label);
        Assert.Equal(
            new[] { "image.half", "image.max1920" },
            resize.Children.Select(child => child.Id).ToArray());

        var archive = Assert.Single(groups, group => group.GroupId == "tools.archive");
        Assert.Equal("Archive", archive.Label);
        Assert.Equal(
            new[] { "pack.zip", "pack.tar.gz" },
            archive.Children.Select(child => child.Id).ToArray());

        Assert.All(groups, group => Assert.True(group.Children.Count >= 2));
    }

    [Fact]
    public void HidingOneChildCollapsesTheGroupToTheRemainingOperation()
    {
        var operations = WheelLayout.Arrange(
            WheelController.BuildOperations(), ["image.rotate.left"], []);
        var slices = WheelGroups.Arrange(operations);

        Assert.DoesNotContain(slices,
            slice => slice is WheelGroupOperation group && group.GroupId == "image.rotate");
        Assert.Contains(slices, slice => slice.Id == "image.rotate.right");
        Assert.DoesNotContain(slices, slice => slice.Id == "image.rotate.left");
    }

    [Fact]
    public void GroupParentIsNeverDispatchedAsAJob()
    {
        var group = WheelSlices().OfType<WheelGroupOperation>().Single(g => g.GroupId == "image.rotate");
        var settings = new AppSettings();
        var jobs = new JobRunner(settings, new ToolLocator());

        var error = Assert.Throws<InvalidOperationException>(() =>
            WheelController.Dispatch(
                group,
                [new FileItem(Path.Combine(Path.GetTempPath(), "group-parent.png"))],
                null,
                jobs));

        Assert.Contains("chooser", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(settings.LastOperationId);
    }
}
