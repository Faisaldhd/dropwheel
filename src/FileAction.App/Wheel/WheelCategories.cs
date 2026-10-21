using FileAction.Core;

namespace FileAction.App.Wheel;

/// <summary>
/// The Tools wheel is a menu of intents, not a list of every operation. Each category is one parent
/// slice; clicking it opens the small chooser with the operations it owns. Six or seven categories stay
/// readable even on a 1366-wide screen.
/// </summary>
internal static class WheelCategories
{
    private sealed record Category(string Id, string LabelKey, string[] Ids, string[] Prefixes);

    private static readonly Category[] Tools =
    [
        new("category.transform", "wheel.category.transform",
            ["tools.crop", "image.square", "image.rotate.right", "image.rotate.left", "image.flip"], []),
        new("category.optimize", "wheel.category.optimize",
            ["tools.compress", "image.compress", "image.half", "image.max1920", "image.strip", "image.grayscale"], []),
        new("category.annotate", "wheel.category.annotate",
            ["tools.annotate", "tools.redact", "tools.editphotos", "tools.addbackground", "tools.removebackground", "image.bg.remove", "tools.collage"], []),
        new("category.metadata", "wheel.category.metadata",
            ["tools.metadata", "tools.qr"], []),
        new("category.video", "wheel.category.video", [], ["tools.video"]),
        new("category.audio", "wheel.category.audio", [], ["tools.audio"]),
        new("category.pdf", "wheel.category.pdf", ["pdf.split", "pdf.rotate", "pdf.optimize"], ["tools.pdf"]),
        new("category.protect", "wheel.category.protect",
            ["tools.protect", "tools.lock", "tools.unlock"], []),
        new("category.archive", "wheel.category.archive",
            ["pack.zip", "pack.tar.gz", "archive.extract"], []),
    ];

    public static IReadOnlyList<Operation> Arrange(IEnumerable<Operation> operations)
    {
        var ordered = operations as IReadOnlyList<Operation> ?? operations.ToList();
        if (ordered.Count == 0 || ordered[0].Wheel != WheelKind.Tools)
            return ordered;

        var buckets = Tools.ToDictionary(category => category, _ => new List<Operation>());
        var unmatched = new List<Operation>();
        foreach (var operation in ordered)
        {
            var category = Tools.FirstOrDefault(candidate =>
                candidate.Ids.Contains(operation.Id, StringComparer.Ordinal) ||
                candidate.Prefixes.Any(prefix => operation.Id.StartsWith(prefix, StringComparison.Ordinal)));
            if (category is null)
                unmatched.Add(operation);
            else
                buckets[category].Add(operation);
        }

        var slices = new List<Operation>(ordered.Count);
        foreach (var category in Tools)
        {
            // Keep the authored order (Crop before Square, Rotate before the rest) instead of whichever
            // order the catalogue happened to emit.
            var children = category.Ids.Length == 0
                ? buckets[category]
                : category.Ids
                    .Select(id => buckets[category].FirstOrDefault(operation => operation.Id == id))
                    .Where(operation => operation is not null)
                    .Cast<Operation>()
                    .Concat(buckets[category].Where(operation => !category.Ids.Contains(operation.Id, StringComparer.Ordinal)))
                    .ToList();
            if (children.Count >= 2)
            {
                var definition = new WheelGroupDefinition(
                    category.Id, children[0].Id, children.Select(child => child.Id).ToArray(), category.LabelKey);
                slices.Add(new WheelGroupOperation(definition, children[0], children));
            }
            else if (children.Count == 1)
            {
                slices.Add(children[0]);
            }
        }
        slices.AddRange(unmatched);
        return slices;
    }
}
