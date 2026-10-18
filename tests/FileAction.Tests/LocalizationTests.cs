using System.Globalization;
using System.Text.RegularExpressions;
using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.Tests;

/// <summary>
/// Guards the shipped string tables. These tests change the process-wide language, so the collection is
/// serialized (see <see cref="LanguageSwitching"/> in TestSetup.cs).
/// </summary>
[Collection(nameof(LanguageSwitching))]
public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static readonly string[] PluralCategories = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>Proper nouns and technical tokens where the Arabic text is legitimately the same as English.</summary>
    private static readonly HashSet<string> EqualByDesign =
    [
        "hotkey.ctrlAltSpace",
        "hotkey.ctrlShiftSpace",
        "settings.language.system",
        "language.english",
        "language.arabic",
        "catalog.pdf.toPptx.subtitle",
        "catalog.pdf.toMd.subtitle",
        "catalog.pdf.toWord.subtitle",
        "catalog.doc.toWord.subtitle",
        "catalog.doc.toMd.subtitle",
        "catalog.sheet.toXlsx.subtitle",
    ];

    /// <summary>Keys the app reads while starting up: tray balloons, tray menu, settings and jobs windows.</summary>
    private static readonly string[] StartupKeys =
    [
        "app.running.title",
        "app.running.hint",
        "app.hotkey.changed.title",
        "app.hotkey.changed.body",
        "app.hotkey.taken.title",
        "app.hotkey.taken.body",
        "app.error.title",
        "help.title",
        "help.body",
        "tray.pause",
        "tray.clipboardWheel",
        "tray.settings",
        "tray.emptyShelf",
        "tray.help",
        "tray.exit",
        "settings.title",
        "settings.header.tagline",
        "settings.language.section",
        "settings.language.app",
        "settings.language.system",
        "settings.gesture.section",
        "settings.gesture.lead",
        "settings.gesture.detail",
        "settings.keyboard.section",
        "settings.results.section",
        "settings.quality.section",
        "settings.system.section",
        "settings.tools.section",
        "settings.footer",
        "jobs.title",
        "job.done",
        "action.cancel",
        "action.close",
        "action.open",
        "action.showInFolder",
        "action.copy.label",
        "action.move.label",
        "action.share.label",
        "wheel.convert",
        "wheel.tools",
        "wheel.actions",
        "hotkey.ctrlAltSpace",
        "hotkey.off",
    ];

    private static void Use(Language language, Action action)
    {
        var previousLanguage = Loc.Current;
        var previousStrict = Loc.StrictMode;
        try
        {
            Loc.Current = language;
            action();
        }
        finally
        {
            Loc.Current = previousLanguage;
            Loc.StrictMode = previousStrict;
        }
    }

    private static T Use<T>(Language language, Func<T> action)
    {
        var previousLanguage = Loc.Current;
        var previousStrict = Loc.StrictMode;
        try
        {
            Loc.Current = language;
            return action();
        }
        finally
        {
            Loc.Current = previousLanguage;
            Loc.StrictMode = previousStrict;
        }
    }

    private static HashSet<string> Placeholders(string value) =>
        Placeholder.Matches(value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    private static bool IsPluralCategoryKey(string key, IReadOnlyDictionary<string, string> table)
    {
        var dot = key.LastIndexOf('.');
        if (dot < 0 || !PluralCategories.Contains(key[(dot + 1)..], StringComparer.Ordinal))
            return false;
        return table.ContainsKey(key[..dot] + ".other");
    }

    [Fact]
    public void Tables_have_the_same_keys()
    {
        var en = Loc.Table(Language.English);
        var ar = Loc.Table(Language.Arabic);
        Assert.True(en.Count > 200, $"only {en.Count} English keys");
        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), ar.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_value_is_empty()
    {
        foreach (var (language, table) in new[]
                 {
                     (Language.English, Loc.Table(Language.English)),
                     (Language.Arabic, Loc.Table(Language.Arabic)),
                 })
        {
            foreach (var (key, value) in table)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: '{key}' is empty");
                Assert.DoesNotContain('\uFFFD', value);
            }
        }
    }

    [Fact]
    public void Placeholder_sets_match()
    {
        var en = Loc.Table(Language.English);
        var ar = Loc.Table(Language.Arabic);
        foreach (var (key, english) in en)
        {
            var arabic = ar[key];
            if (IsPluralCategoryKey(key, en))
            {
                // Arabic legitimately drops {0} for the singular/dual ("عنصر واحد", "عنصران").
                // It must never invent a placeholder the group does not have.
                var baseKey = key[..key.LastIndexOf('.')];
                var allowed = Placeholders(en[baseKey + ".other"]);
                Assert.True(Placeholders(arabic).IsSubsetOf(allowed), $"'{key}' invents placeholders: {arabic}");
            }
            else
            {
                Assert.Equal(Placeholders(english), Placeholders(arabic));
            }
        }
    }

    [Fact]
    public void Arabic_is_not_a_copy_of_english()
    {
        var en = Loc.Table(Language.English);
        var ar = Loc.Table(Language.Arabic);
        foreach (var (key, english) in en)
            if (!EqualByDesign.Contains(key))
                Assert.NotEqual(english, ar[key]);
    }

    [Fact]
    public void Every_plural_group_has_all_six_categories()
    {
        Assert.NotEmpty(Loc.PluralBaseKeys);
        foreach (var baseKey in Loc.PluralBaseKeys)
        {
            Assert.True(baseKey is "items.count" or "job.doneCount", baseKey);
            foreach (var category in PluralCategories)
            {
                Assert.True(Loc.HasKey($"{baseKey}.{category}", Language.English), $"en {baseKey}.{category}");
                Assert.True(Loc.HasKey($"{baseKey}.{category}", Language.Arabic), $"ar {baseKey}.{category}");
            }
        }
    }

    [Fact]
    public void English_plurals()
    {
        Use(Language.English, () =>
        {
            Assert.Equal("1 item", Loc.Items(1));
            Assert.Equal("3 items", Loc.Items(3));
            Assert.Equal("0 items", Loc.Items(0));
            Assert.Equal("Done: 3 outputs", Loc.Plural("job.doneCount", 3));
        });
    }

    [Fact]
    public void Arabic_plurals()
    {
        Use(Language.Arabic, () =>
        {
            Assert.Equal("لا عناصر", Loc.Items(0));
            Assert.Equal("عنصر واحد", Loc.Items(1));
            Assert.Equal("عنصران", Loc.Items(2));
            Assert.Equal("3 عناصر", Loc.Items(3));
            Assert.Equal("11 عنصرًا", Loc.Items(11));
            Assert.Equal("100 عنصر", Loc.Items(100));

            // 103 % 100 = 3 → "few"; 111 % 100 = 11 → "many".
            Assert.Equal("103 عناصر", Loc.Items(103));
            Assert.Equal("111 عنصرًا", Loc.Items(111));

            Assert.Equal("تم: مخرجان", Loc.Plural("job.doneCount", 2));
            Assert.Equal("تم: 5 مخرجات", Loc.Plural("job.doneCount", 5));
        });
    }

    [Fact]
    public void Arabic_plural_digits_are_western()
    {
        Use(Language.Arabic, () =>
        {
            foreach (var n in new[] { 0, 1, 2, 3, 11, 100 })
            {
                var text = Loc.Items(n);
                Assert.False(text.Any(c => c is >= '\u0660' and <= '\u0669'), text);
                if (n is not (0 or 1 or 2))
                    Assert.Contains(n.ToString(CultureInfo.InvariantCulture), text);
            }
        });
    }

    [Fact]
    public void Catalog_builds_in_both_languages_without_missing_keys()
    {
        var previousLanguage = Loc.Current;
        var previousStrict = Loc.StrictMode;
        try
        {
            Loc.StrictMode = true;

            Loc.Current = Language.English;
            var english = Catalog.Build();

            Loc.Current = Language.Arabic;
            var arabic = Catalog.Build();

            Assert.Equal(english.Count, arabic.Count);
            Assert.Equal(english.Select(o => o.Id), arabic.Select(o => o.Id));
            Assert.DoesNotContain(arabic, o => string.IsNullOrWhiteSpace(o.Label));
            Assert.DoesNotContain(english, o => string.IsNullOrWhiteSpace(o.Label));

            Assert.Equal("ضغط", arabic.Single(o => o.Id == "image.compress").Label);
            Assert.Equal("اقتصاص", arabic.Single(o => o.Id == "video.trim").Label);
            Assert.Equal("بحد أقصى 1920 بكسل", arabic.Single(o => o.Id == "image.max1920").Label);
            Assert.Equal("إلى اليمين", arabic.Single(o => o.Id == "image.rotate.right").Subtitle);
            Assert.Equal("Compress", english.Single(o => o.Id == "image.compress").Label);
            Assert.Equal("Trim", english.Single(o => o.Id == "video.trim").Label);

            // Parameter labels and hints are localized too.
            var trim = arabic.Single(o => o.Id == "video.trim");
            Assert.Equal(new[] { "البداية", "النهاية" }, trim.Parameters.Select(p => p.Label));
            Assert.StartsWith("مثل", trim.Parameters.First(p => p.Key == "start").Hint);
        }
        finally
        {
            Loc.Current = previousLanguage;
            Loc.StrictMode = previousStrict;
        }
    }

    [Fact]
    public void Startup_labels_resolve_in_both_languages()
    {
        var previousLanguage = Loc.Current;
        var previousStrict = Loc.StrictMode;
        try
        {
            Loc.StrictMode = true;
            foreach (var language in new[] { Language.English, Language.Arabic })
            {
                Loc.Current = language;
                foreach (var key in StartupKeys)
                {
                    var value = Loc.T(key);
                    Assert.NotEqual(key, value);
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: '{key}'");
                }
            }
        }
        finally
        {
            Loc.Current = previousLanguage;
            Loc.StrictMode = previousStrict;
        }
    }

    [Fact]
    public void Missing_key_throws_in_strict_mode_and_returns_the_key_in_the_app()
    {
        var previousStrict = Loc.StrictMode;
        try
        {
            Loc.Current = Language.English;
            Loc.StrictMode = true;
            var ex = Assert.Throws<MissingLocalizationKeyException>(() => Loc.T("test.key.that.does.not.exist"));
            Assert.Equal("test.key.that.does.not.exist", ex.Key);
            Assert.Equal(Language.English, ex.Language);

            Loc.StrictMode = false;
            Assert.Equal("test.key.that.does.not.exist", Loc.T("test.key.that.does.not.exist"));
        }
        finally
        {
            Loc.StrictMode = previousStrict;
        }
    }

    [Fact]
    public void Formatted_calls_reach_the_key_api_even_with_one_string_argument()
    {
        // Regression guard: the legacy two-language overload must not swallow T(key, string).
        Use(Language.Arabic, () => Assert.Equal("خطأ في الملف: boom", Loc.T("error.file", "boom")));
        Use(Language.English, () => Assert.Equal("Convert to PDF", Loc.T("job.title.convertTo", "PDF")));

        // The old call shape still returns English.
        Assert.Equal("English", Loc.T("česky", "English"));
    }

    [Fact]
    public void Legacy_overload_directly_returns_english()
    {
        var legacy = typeof(Loc).GetMethod("T", [typeof(string), typeof(string)]);
        Assert.NotNull(legacy);
        Assert.True(legacy.IsDefined(typeof(ObsoleteAttribute), inherit: false));
        Assert.Equal("English", (string?)legacy.Invoke(null, ["česky", "English"]));
    }

    [Fact]
    public void System_language_follows_the_ui_culture()
    {
        var culture = CultureInfo.CurrentUICulture;
        var previousLanguage = Loc.Current;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar-EG");
            Loc.Current = Language.System;
            Assert.Equal(Language.Arabic, Loc.Current);
            Assert.Equal(Language.Arabic, Loc.Detect());
            Assert.True(Loc.IsRtl);

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Loc.Current = Language.System;
            Assert.Equal(Language.English, Loc.Current);
            Assert.False(Loc.IsRtl);
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
            Loc.Current = previousLanguage;
        }
    }

    [Fact]
    public void Arabic_values_use_balanced_bidi_isolates()
    {
        var ar = Loc.Table(Language.Arabic);
        foreach (var (key, value) in ar.Where(p => p.Value.Contains('\u2066')))
        {
            Assert.Equal(value.Count(c => c == '\u2066'), value.Count(c => c == '\u2069'));
            Assert.True(value.All(c => c != '\u202A' && c != '\u202C'), key);
        }
    }
}
