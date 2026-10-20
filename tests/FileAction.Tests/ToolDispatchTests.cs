using FileAction.App.UI.Tools;
using FileAction.App.Wheel;

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
/// </summary>
public class ToolDispatchTests
{
    [Fact]
    public void EveryRegisteredToolWindowIsReachableFromTheWheel()
    {
        var operations = WheelController.BuildOperations();
        var byId = operations
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
        var duplicates = WheelController.BuildOperations()
            .GroupBy(operation => operation.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' x{group.Count()}")
            .ToArray();

        Assert.True(duplicates.Length == 0,
            "two wheel operations share an id (last-used-operation memory and the wheel editor key on it): "
            + string.Join(", ", duplicates));
    }
}
