using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.Wheel;

/// <summary>
/// Related wheel operations, folded into one parent slice. The parent is display-only: choosing it opens
/// a small chooser with the children, so no operation is removed and every child stays reachable.
/// Definitions name the operation whose label and glyph represent the parent (for example Crop covers
/// Crop and Crop Square), and the ids that belong together.
/// </summary>
internal sealed record WheelGroupDefinition(
    string Id,
    string TitleOperationId,
    IReadOnlyList<string> OperationIds,
    string? LabelKey = null);

/// <summary>
/// The App wheel's grouping rules. Kept as data so the UI and the tests use exactly the same definitions.
/// </summary>
internal static class WheelGroups
{
    public static IReadOnlyList<WheelGroupDefinition> Default { get; } =
    [
        new("image.crop", "tools.crop", ["tools.crop", "image.square"]),
        new("image.rotate", "image.rotate.right", ["image.rotate.right", "image.rotate.left"]),
        new("image.resize", "image.half", ["image.half", "image.max1920"], "wheel.group.resize"),
    ];

    /// <summary>
    /// Folds the visible operations into parent slices. Only two or more visible children form a group;
    /// a group that is hidden down to one child collapses back to that ordinary operation.
    /// </summary>
    public static IReadOnlyList<Operation> Arrange(IEnumerable<Operation> operations)
    {
        var ordered = operations as IReadOnlyList<Operation> ?? operations.ToList();
        var byId = ordered
            .GroupBy(operation => operation.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var slices = new List<Operation>(ordered.Count);

        foreach (var operation in ordered)
        {
            if (consumed.Contains(operation.Id))
                continue;

            var definition = Default.FirstOrDefault(candidate => candidate.OperationIds.Contains(operation.Id, StringComparer.Ordinal));
            if (definition is null)
            {
                slices.Add(operation);
                continue;
            }

            // Keep the definition's order (Crop before Square, right before left) and ignore failures to
            // find a child; a hidden or unavailable child was already filtered out by WheelLayout.
            var children = definition.OperationIds
                .Select(id => byId.GetValueOrDefault(id))
                .Where(child => child is not null && child.Wheel == operation.Wheel)
                .Cast<Operation>()
                .ToList();
            foreach (var child in children)
                consumed.Add(child.Id);

            if (children.Count >= 2)
            {
                var title = children.FirstOrDefault(child => child.Id == definition.TitleOperationId) ?? children[0];
                slices.Add(new WheelGroupOperation(definition, title, children));
            }
            else if (children.Count == 1)
            {
                slices.Add(children[0]);
            }
            else
            {
                slices.Add(operation);
            }
        }

        return slices;
    }

    /// <summary>The operations behind a list of slices, in the order the chooser shows them.</summary>
    public static IReadOnlyList<Operation> Flatten(IEnumerable<Operation> slices)
    {
        var operations = new List<Operation>();
        foreach (var slice in slices)
        {
            if (slice is WheelGroupOperation group)
                operations.AddRange(group.Children);
            else
                operations.Add(slice);
        }
        return operations;
    }
}

/// <summary>A parent wheel slice. It is never executed: the wheel opens a chooser with <see cref="Children"/>.</summary>
internal sealed class WheelGroupOperation : Operation
{
    public WheelGroupOperation(WheelGroupDefinition definition, Operation title, IReadOnlyList<Operation> children)
        : base($"wheel.group.{definition.Id}", title.Wheel, Label(definition, title), title.Glyph, Items(children.Count))
    {
        GroupId = definition.Id;
        Title = title;
        Children = children;
    }

    public string GroupId { get; }

    /// <summary>The operation whose label and glyph stand for the group (Crop, Rotate).</summary>
    public Operation Title { get; }

    public IReadOnlyList<Operation> Children { get; }

    private static string Label(WheelGroupDefinition definition, Operation title) =>
        definition.LabelKey is null ? title.Label : T(definition.LabelKey);

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => false;

    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        throw new InvalidOperationException("A wheel group opens its chooser; it never runs as a job.");
}
