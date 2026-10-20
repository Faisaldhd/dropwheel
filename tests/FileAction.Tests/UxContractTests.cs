using FileAction.App.UI.Tools;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// UX contracts that are cheap to check and expensive to miss. These are data-only: no window is
/// created, no media is decoded, nothing is written. They cover two rules from
/// <c>docs/audit/UX-AUDIT.md</c> §13:
///
/// 1. every registered tool resolves a window title and a primary-action label in English and Arabic,
///    and no value falls back to its own key;
/// 2. no two tools share a display title.
///
/// Adding a tool without its strings fails here instead of shipping a window titled `tool.foo.title`.
/// (Title Casing across the existing labels is a separate, owner-approved string pass; it is deliberately
/// not enforced here because the string tables are append-only for this lane.)
/// </summary>
[Collection(nameof(LanguageSwitching))]
public class UxContractTests
{
    [Fact]
    public void EveryRegisteredToolResolvesItsTitleAndPrimaryLabelInBothLanguages()
    {
        var specs = ToolRegistry.All;
        Assert.NotEmpty(specs);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in specs)
        {
            Assert.StartsWith("tools.", spec.Id, StringComparison.Ordinal);
            Assert.True(ids.Add(spec.Id), $"duplicate tool id '{spec.Id}'");
            Assert.True(spec.Layout.ChromeHeight > 0, $"{spec.Id} declares no chrome height");
        }

        foreach (var language in new[] { Language.English, Language.Arabic })
        {
            var previous = Loc.Current;
            try
            {
                Loc.Current = language;
                foreach (var spec in specs)
                {
                    AssertResolves(spec.Id, spec.TitleKey, "title", language);
                    AssertResolves(spec.Id, spec.PrimaryLabelKey, "primary label", language);
                }
            }
            finally
            {
                Loc.Current = previous;
            }
        }
    }

    [Fact]
    public void ToolTitlesAreUnique()
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = Language.English;
            var duplicates = ToolRegistry.All
                .GroupBy(spec => Loc.T(spec.TitleKey), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => $"'{group.Key}' ({string.Join(", ", group.Select(spec => spec.Id))})")
                .ToArray();

            Assert.True(duplicates.Length == 0,
                "two tool windows share a display title: " + string.Join("; ", duplicates));
        }
        finally
        {
            Loc.Current = previous;
        }
    }

    private static void AssertResolves(string toolId, string key, string role, Language language)
    {
        Assert.False(string.IsNullOrWhiteSpace(key), $"{toolId} declares no {role} key");

        var text = Loc.T(key);
        Assert.False(string.IsNullOrWhiteSpace(text), $"{toolId}: {role} '{key}' is empty in {language}");
        Assert.True(text != key, $"{toolId}: {role} '{key}' is missing from the {language} string table");
    }
}
