using FileAction.App.UI.Tools;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// UX contracts that are cheap to check and expensive to miss. These are data-only: no window is
/// created, no media is decoded, nothing is written. They cover the three rules from
/// <c>docs/audit/UX-AUDIT.md</c> §13:
///
/// 1. every registered tool resolves a window title and a primary-action label in English and Arabic;
/// 2. every English tool title is Title Case (the mechanical half of FAIL F-12);
/// 3. no two tools share a display title.
///
/// Adding a tool without its strings, or shipping a sentence-case title next to "Crop Image", fails here
/// instead of shipping.
/// </summary>
[Collection(nameof(LanguageSwitching))]
public class UxContractTests
{
    /// <summary>Lowercase words that may appear inside a Title Case string ("Merge to PDF").</summary>
    private static readonly HashSet<string> TitleConnectors = new(StringComparer.Ordinal)
    {
        "to", "and", "of", "with", "for",
    };

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
    public void EnglishToolTitlesAreTitleCase()
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = Language.English;
            foreach (var spec in ToolRegistry.All)
            {
                var title = Loc.T(spec.TitleKey);
                Assert.False(string.IsNullOrWhiteSpace(title) || title == spec.TitleKey,
                    $"{spec.Id} has no English title");

                foreach (var word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TitleConnectors.Contains(word))
                        continue;

                    var letters = word.Where(c => char.IsLetter(c)).ToArray();
                    if (letters.Length == 0)
                        continue;

                    // "QR", "PDF", "BG", "N": an all-caps token is already title-styled.
                    if (letters.All(c => char.IsUpper(c)))
                        continue;

                    Assert.True(char.IsUpper(letters[0]),
                        $"{spec.Id}: title '{title}' contains the lowercase word '{word}'");
                }
            }
        }
        finally
        {
            Loc.Current = previous;
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
