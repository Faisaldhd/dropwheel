using System.Text.RegularExpressions;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// L10N / RTL audit gate for the 2.4.0 string tables — the machine-checkable half of
/// <c>docs/audit/L10N-AUDIT.md</c>. It deliberately does not change <see cref="Loc.Current"/>, so it is
/// deterministic, needs no UI thread and adds no measurable time to CI.
///
/// The append-only rule: a released key may never be removed or renamed. New strings are added to
/// <c>en.json</c> and <c>ar.json</c> in the same change. The historical proof is in the audit
/// (<c>git show v2.1.0..v2.3.0</c> vs <c>main</c>: 0 keys removed); the runtime gate is
/// <see cref="No_english_key_was_removed_from_the_arabic_table"/> plus
/// <see cref="No_key_exists_in_only_one_table"/>, which fail the build if either table loses a key.
/// </summary>
public class LocalizationAuditTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex RawKey = new(@"^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9]+){2,}$", RegexOptions.Compiled);
    private static readonly Regex ArabicLetter = new(@"[\u0600-\u06FF]", RegexOptions.Compiled);
    private static readonly Regex LiteralVersion = new(@"(?<![\d.])\d+\.\d+\.\d+(?![\d.])", RegexOptions.Compiled);

    /// <summary>Product names, format names, key names and aspect ratios that are identical by design.</summary>
    private static readonly HashSet<string> EqualByDesign = new(StringComparer.Ordinal)
    {
        "app.editor.group.pdf",
        "catalog.doc.toMd.subtitle",
        "catalog.doc.toWord.subtitle",
        "catalog.pdf.toMd.subtitle",
        "catalog.pdf.toPptx.subtitle",
        "catalog.pdf.toWord.subtitle",
        "catalog.sheet.toXlsx.subtitle",
        "hotkey.ctrlAltSpace",
        "hotkey.ctrlShiftSpace",
        "language.arabic",
        "language.english",
        "settings.language.system",
        "tool.crop.aspect.16x9",
        "tool.crop.aspect.1x1",
        "tool.crop.aspect.3x4",
        "tool.crop.aspect.4x3",
        "tool.crop.aspect.9x16",
    };

    /// <summary>Arabic values that intentionally carry no Arabic letters (a bare isolated placeholder).</summary>
    private static readonly HashSet<string> NoArabicByDesign = new(StringComparer.Ordinal) { "update.error.body" };

    /// <summary>
    /// Keys whose <c>{0}</c> is a version (or a build time from the same updater). Arabic must wrap the
    /// interpolated technical run in U+2066 … U+2069 so "2.4.0" cannot reorder inside the sentence.
    /// </summary>
    private static readonly string[] VersionKeys =
    [
        "settings.about.version",
        "settings.updates.available",
        "settings.updates.current",
        "settings.updates.lastChecked",
        "settings.updates.upToDate",
        "update.available.body",
        "update.check.upToDate",
        "update.download.title",
        "update.error.body",
        "update.menu",
        "update.updated.body",
    ];

    private static readonly string[] PluralCategories = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>Every key group the 2.4.0 UI reads; "about" lives under <c>settings.about.</c>.</summary>
    private static readonly string[] RequiredPrefixes =
    [
        "action.", "app.", "catalog.", "editor.", "error.", "fileType.", "help.", "hotkey.", "items.",
        "job.", "jobs.", "language.", "ocr.", "param.", "preview.", "settings.", "settings.about.",
        "sheet.", "shelf.", "suffix.", "toast.", "tool.", "tray.", "update.", "wheel.",
    ];

    private static IReadOnlyDictionary<string, string> En => Loc.Table(Language.English);
    private static IReadOnlyDictionary<string, string> Ar => Loc.Table(Language.Arabic);

    private static List<string> Placeholders(string value) =>
        Placeholder.Matches(value).Select(m => m.Groups[1].Value).ToList();

    private static bool IsPluralCategoryKey(string key, IReadOnlyDictionary<string, string> table)
    {
        var dot = key.LastIndexOf('.');
        if (dot < 0 || !PluralCategories.Contains(key[(dot + 1)..], StringComparer.Ordinal))
            return false;
        return table.ContainsKey(key[..dot] + ".other");
    }

    [Fact]
    public void Tables_have_exact_key_parity_in_both_directions()
    {
        var en = En;
        var ar = Ar;

        // 2.4.0 shipped 1094 keys; the floor only rises (append-only rule).
        Assert.True(en.Count >= 1000, $"only {en.Count} English keys");
        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), ar.Keys.Order(StringComparer.Ordinal));

        foreach (var prefix in RequiredPrefixes)
        {
            Assert.Contains(en.Keys, key => key.StartsWith(prefix, StringComparison.Ordinal));
            Assert.Contains(ar.Keys, key => key.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void No_english_key_was_removed_from_the_arabic_table()
    {
        var removed = En.Keys.Where(key => !Ar.ContainsKey(key)).Order(StringComparer.Ordinal).ToList();
        Assert.True(removed.Count == 0,
            "Append-only rule: these English keys no longer exist in ar.json:" + Environment.NewLine
            + "  " + string.Join(Environment.NewLine + "  ", removed));
    }

    [Fact]
    public void No_key_exists_in_only_one_table()
    {
        var oneSided = En.Keys.Except(Ar.Keys, StringComparer.Ordinal)
            .Concat(Ar.Keys.Except(En.Keys, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(oneSided.Count == 0,
            "A new key must be added to BOTH en.json and ar.json (never to one table only):" + Environment.NewLine
            + "  " + string.Join(Environment.NewLine + "  ", oneSided));
    }

    [Fact]
    public void No_value_is_empty_or_a_raw_localization_key()
    {
        foreach (var (language, table) in new[] { (Language.English, En), (Language.Arabic, Ar) })
        {
            foreach (var (key, value) in table)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: '{key}' is empty");
                Assert.DoesNotContain('\uFFFD', value);
                Assert.False(RawKey.IsMatch(value.Trim()),
                    $"{language}: '{key}' renders a raw key instead of text: '{value}'");
            }
        }
    }

    [Fact]
    public void No_user_facing_prose_is_left_untranslated()
    {
        var en = En;
        var ar = Ar;
        var problems = new List<string>();

        foreach (var (key, english) in en)
        {
            if (!ar.TryGetValue(key, out var arabic))
                continue;

            if (english == arabic && !EqualByDesign.Contains(key))
                problems.Add($"{key}: Arabic equals English for real prose ('{arabic}')");
            else if (!ArabicLetter.IsMatch(arabic) && !EqualByDesign.Contains(key) && !NoArabicByDesign.Contains(key))
                problems.Add($"{key}: Arabic value has no Arabic letters ('{arabic}')");
        }

        // A stale allowlist must not silently exempt a key that is no longer identical.
        foreach (var key in EqualByDesign.Where(key => !en.ContainsKey(key)))
            problems.Add($"{key}: allowlisted but no longer in en.json (remove it from EqualByDesign)");

        Assert.True(problems.Count == 0,
            "Untranslated Arabic:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", problems));
    }

    [Fact]
    public void Placeholder_multisets_match_per_key()
    {
        var en = En;
        var ar = Ar;
        var problems = new List<string>();

        foreach (var (key, english) in en)
        {
            if (!ar.TryGetValue(key, out var arabic))
                continue;

            var enPlaceholders = Placeholders(english);
            var arPlaceholders = Placeholders(arabic);

            if (IsPluralCategoryKey(key, en))
            {
                // Arabic legitimately drops {0} for the singular and dual ("عنصر واحد", "عنصران");
                // it must never invent a placeholder the group does not have.
                var baseKey = key[..key.LastIndexOf('.')];
                var allowed = Placeholders(en[baseKey + ".other"]).ToHashSet(StringComparer.Ordinal);
                var invented = arPlaceholders.Where(p => !allowed.Contains(p)).ToList();
                if (invented.Count > 0)
                    problems.Add($"{key}: invents placeholder(s) {string.Join(", ", invented)} ('{arabic}')");
            }
            else if (!enPlaceholders.SequenceEqual(arPlaceholders))
            {
                problems.Add($"{key}: en [{string.Join(", ", enPlaceholders)}] vs ar [{string.Join(", ", arPlaceholders)}]");
            }
        }

        Assert.True(problems.Count == 0,
            "Placeholder/format-argument parity broken:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", problems));
    }

    [Fact]
    public void Version_interpolations_are_wrapped_in_bidi_isolates()
    {
        var ar = Ar;
        var problems = new List<string>();

        foreach (var key in VersionKeys)
        {
            Assert.True(ar.ContainsKey(key), $"missing Arabic version key '{key}'");
            var value = ar[key];

            // Every placeholder must be the direct content of an LRI … PDI pair.
            var withoutIsolates = Regex.Replace(value, "\u2066[^\u2069]*\u2069", string.Empty);
            var bare = Placeholders(withoutIsolates);
            if (bare.Count > 0)
                problems.Add($"{key}: placeholder(s) {string.Join(", ", bare)} outside U+2066 … U+2069 ('{value}')");

            // A literal version must also be isolated; version-like text inside an isolate is already stripped.
            if (LiteralVersion.IsMatch(withoutIsolates))
                problems.Add($"{key}: literal version outside a bidi isolate ('{value}')");
        }

        // Belt and braces: every Arabic value with an isolate keeps them balanced.
        foreach (var (key, value) in ar)
        {
            if (value.Count(c => c == '\u2066') != value.Count(c => c == '\u2069'))
                problems.Add($"{key}: unbalanced bidi isolates ('{value}')");
        }

        Assert.True(problems.Count == 0,
            "Arabic version/number bidi isolation:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", problems));
    }

    [Fact]
    public void Arabic_uses_western_digits_only()
    {
        var problems = Ar
            .Where(p => p.Value.Any(c => c is >= '\u0660' and <= '\u0669' or >= '\u06F0' and <= '\u06F9'))
            .Select(p => $"{p.Key}: '{p.Value}'")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(problems.Count == 0,
            "Arabic-Indic digits are not allowed; use Western 0–9:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", problems));
    }

    [Fact]
    public void Bidi_isolates_are_balanced_and_no_legacy_directional_marks_are_used()
    {
        var problems = new List<string>();
        foreach (var (key, value) in Ar)
        {
            if (value.Count(c => c == '\u2066') != value.Count(c => c == '\u2069'))
                problems.Add($"{key}: unbalanced U+2066/U+2069 ('{value}')");
            if (value.Any(c => c is '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E'))
                problems.Add($"{key}: legacy embedding/override mark ('{value}')");
            if (value.Any(c => c is '\u200E' or '\u200F'))
                problems.Add($"{key}: LRM/RLM mark; use U+2066…U+2069 isolates instead ('{value}')");
        }

        Assert.True(problems.Count == 0,
            "Bidi isolation hygiene:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", problems));
    }
}
