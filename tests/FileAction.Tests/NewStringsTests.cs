using System.Text.Json;
using System.Text.RegularExpressions;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// 2.2.0 string-table acceptance: the six tab keys, the general/theme/updates/about groups and the new
/// update notifications must exist in both languages with matching placeholders. Values are read from the
/// committed <c>en.json</c>/<c>ar.json</c> so the test covers exactly what ships.
/// </summary>
public class NewStringsTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>Every string the 2.2.0 UI and updater read through <c>Loc.T("key")</c>.</summary>
    private static readonly string[] RequiredKeys =
    [
        // Six settings tabs.
        "settings.tab.general",
        "settings.tab.wheels",
        "settings.tab.quality",
        "settings.tab.integration",
        "settings.tab.updates",
        "settings.tab.about",

        // General page.
        "settings.general.watch.title",
        "settings.general.watch.detail",
        "settings.general.watch.policy",
        "settings.general.language.title",
        "settings.general.language.detail",
        "settings.general.theme.title",
        "settings.general.theme.detail",
        "settings.general.gestures.title",
        "settings.general.gestures.detail",

        // Theme choices.
        "settings.theme.system",
        "settings.theme.dark",
        "settings.theme.light",

        // Updates page.
        "settings.updates.title",
        "settings.updates.detail",
        "settings.updates.automatic.title",
        "settings.updates.automatic.detail",
        "settings.updates.check",
        "settings.updates.checking",
        "settings.updates.upToDate",
        "settings.updates.available",
        "settings.updates.updateNow",
        "settings.updates.whatsNew",
        "settings.updates.downloading",
        "settings.updates.verifying",
        "settings.updates.failed",
        "settings.updates.verified",
        "settings.updates.ready",
        "settings.updates.lastChecked",
        "settings.updates.never",
        "settings.updates.current",
        "settings.updates.unavailable",

        // About page.
        "settings.about.version",
        "settings.about.licenses",
        "settings.about.source",
        "settings.about.credits",
        "settings.about.tagline",
        "settings.about.website",

        // Update notifications / tray.
        "update.updated.title",
        "update.updated.body",
        "update.updated.whatsNew",
        "update.error.title",
        "update.error.body",
        "update.download.title",
        "update.download.body",
        "update.download.cancel",
        "update.download.failed",
        "update.verify.failed",
        "update.restart",
        "update.check.upToDate",
        "update.check.failed",
        "update.tray.check",
    ];

    private static readonly string[] NewKeyPrefixes =
    [
        "settings.tab.",
        "settings.general.",
        "settings.theme.",
        "settings.updates.",
        "settings.about.",
        "update.",
    ];

    [Fact]
    public void Both_tables_contain_every_2_2_0_key()
    {
        var (en, ar) = LoadTables();

        foreach (var key in RequiredKeys)
        {
            Assert.True(en.ContainsKey(key), $"en.json is missing the 2.2.0 key '{key}'.");
            Assert.True(ar.ContainsKey(key), $"ar.json is missing the 2.2.0 key '{key}'.");
        }
    }

    [Fact]
    public void New_key_sets_match_between_english_and_arabic()
    {
        var (en, ar) = LoadTables();

        Assert.Equal(NewKeys(en), NewKeys(ar));

        static string[] NewKeys(Dictionary<string, string> table) =>
            table.Keys
                .Where(key => NewKeyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToArray();
    }

    [Fact]
    public void Placeholders_match_for_every_required_key()
    {
        var (en, ar) = LoadTables();

        foreach (var key in RequiredKeys)
            Assert.Equal(Placeholders(en[key]), Placeholders(ar[key]));
    }

    [Fact]
    public void Arabic_upToDate_wraps_the_version_in_bidi_isolates()
    {
        var (_, ar) = LoadTables();
        var value = ar["settings.updates.upToDate"];

        Assert.True(
            value.Contains("\u2066{0}\u2069", StringComparison.Ordinal),
            $"Arabic settings.updates.upToDate must wrap {{0}} in U+2066/U+2069, actual: {value}");
        Assert.Equal(value.Count(c => c == '\u2066'), value.Count(c => c == '\u2069'));
    }

    [Fact]
    public void Tables_are_at_least_the_committed_2_2_0_size()
    {
        var (en, ar) = LoadTables();

        Assert.True(en.Count >= 346, $"en.json has {en.Count} keys; the 2.2.0 tables have 346.");
        Assert.Equal(en.Count, ar.Count);
    }

    private static (Dictionary<string, string> English, Dictionary<string, string> Arabic) LoadTables()
    {
        var strings = Path.Combine(RepoRoot(), "src", "FileAction.Core", "Strings");
        return (Load(Path.Combine(strings, "en.json")), Load(Path.Combine(strings, "ar.json")));
    }

    private static Dictionary<string, string> Load(string path)
    {
        Assert.True(File.Exists(path), $"Missing string table: {path}");
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(table);
        return table!;
    }

    [Fact]
    public void Loc_loads_the_embedded_tables_and_never_shows_the_key()
    {
        Assert.True(Loc.KeyCount >= 300, $"Loc loaded only {Loc.KeyCount} keys; the embedded tables are broken.");
        Assert.False(
            Loc.T("settings.title").Contains("settings.title", StringComparison.Ordinal),
            "Loc.T(\"settings.title\") returned the key instead of a translated title.");
    }

    private static string[] Placeholders(string value) =>
        Placeholder.Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
