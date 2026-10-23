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
    /// <summary>The slices the Tools wheel actually renders, after hiding, favourites and categories.</summary>
    private static IReadOnlyList<Operation> WheelSlices() =>
        WheelCategories.Arrange(WheelLayout.Arrange(
            WheelController.BuildOperations().Where(operation => operation.Wheel == WheelKind.Tools), [], []));

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
        var operations = WheelLayout.Arrange(
            WheelController.BuildOperations().Where(operation => operation.Wheel == WheelKind.Tools), [], []);
        var flattened = WheelGroups.Flatten(WheelCategories.Arrange(operations));

        Assert.Equal(
            operations.Select(operation => operation.Id).Order(StringComparer.Ordinal),
            flattened.Select(operation => operation.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ImageToolsAreSevenIntentCategoriesNotEighteenSlices()
    {
        var slices = WheelSlices();
        var groups = slices.OfType<WheelGroupOperation>().ToArray();

        var transform = Assert.Single(groups, group => group.GroupId == "category.transform");
        Assert.Equal("Crop & rotate", transform.Label);
        Assert.Equal(
            new[] { "tools.crop", "image.square", "image.rotate.right", "image.rotate.left", "image.flip" },
            transform.Children.Select(child => child.Id).ToArray());

        var optimize = Assert.Single(groups, group => group.GroupId == "category.optimize");
        Assert.Equal("Optimize", optimize.Label);
        Assert.Contains(optimize.Children, child => child.Id == "tools.compress");
        Assert.Contains(optimize.Children, child => child.Id == "image.max1920");

        var annotate = Assert.Single(groups, group => group.GroupId == "category.annotate");
        Assert.Contains(annotate.Children, child => child.Id == "tools.annotate");
        Assert.Contains(annotate.Children, child => child.Id == "tools.redact");
        Assert.Contains(annotate.Children, child => child.Id == "tools.removebackground");
        Assert.Contains(annotate.Children, child => child.Id == "image.bg.remove");
        Assert.Contains(annotate.Children, child => child.Id == "tools.ocr");

        // A category with one applicable tool stays a category (one-slice ring) instead of turning into
        // a direct operation that silently has no children.
        Assert.All(groups, group => Assert.True(group.Children.Count >= 1));
        Assert.All(slices, slice => Assert.DoesNotContain("image.to.", slice.Id));
    }

    [Fact]
    public void Every_tools_slice_opens_an_inner_ring()
    {
        var slices = WheelSlices();
        Assert.All(slices, slice => Assert.IsType<WheelGroupOperation>(slice));
    }

    [Fact]
    public void Remove_background_is_a_reachable_interactive_tool()
    {
        var operation = WheelGroups.Flatten(WheelSlices()).Single(candidate => candidate.Id == "tools.removebackground");
        Assert.IsAssignableFrom<IInteractiveTool>(operation);

        var photo = new FileItem(Path.Combine(Path.GetTempPath(), "photo.png"));
        var spec = ToolRegistry.For("tools.removebackground", [photo]);
        Assert.NotNull(spec);
        Assert.Equal("tools.removebackground", spec.Id);
        Assert.True(spec.Applies([photo]));
    }

    [Fact]
    public void GroupParentIsNeverDispatchedAsAJob()
    {
        var group = WheelSlices().OfType<WheelGroupOperation>().Single(g => g.GroupId == "category.transform");
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
