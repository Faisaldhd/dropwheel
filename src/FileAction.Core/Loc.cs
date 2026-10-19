using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

#pragma warning disable CS0618 // Language.Czech is intentionally still mapped for settings compatibility.

namespace FileAction.Core;

/// <summary>
/// UI language. <see cref="System"/> follows the Windows display language; the two real languages are
/// English and Arabic. <see cref="Czech"/> is a legacy value kept only so that settings files and the
/// current app build keep compiling until the language picker is migrated; it resolves to English.
/// </summary>
public enum Language
{
    System,
    English,
    Arabic,

    [Obsolete("Czech is no longer a UI language. The value is kept for settings compatibility until the language picker is migrated; it resolves to English.")]
    Czech,
}

/// <summary>Thrown by <see cref="Loc.T(string)"/> when a key is missing and <see cref="Loc.StrictMode"/> is on.</summary>
public sealed class MissingLocalizationKeyException(string key, Language language)
    : Exception($"Missing localization key '{key}' for language {language}.")
{
    public string Key { get; } = key;

    public Language Language { get; } = language;
}

/// <summary>
/// Key-based localization backed by the embedded <c>Strings/en.json</c> and <c>Strings/ar.json</c> tables.
/// The tables are loaded once per language on first use. Missing keys never crash the UI: the key itself
/// is shown and logged once; tests turn on <see cref="StrictMode"/> so a missing key fails loudly.
///
/// Placeholders use <c>{0}</c>, <c>{1}</c>… and are formatted with invariant culture, so numbers keep
/// Western digits in Arabic too.
///
/// The old <c>T(czech, english)</c> call shape still works: the legacy overload is kept for source
/// compatibility (and marked obsolete), and the prioritized <see cref="T(string, object?[])"/> overload
/// recognises such a call by an unknown key whose only argument is the English text.
/// </summary>
public static class Loc
{
    private const string ResourcePrefix = "FileAction.Core.Strings.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishTable = new(() => Load("en"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ArabicTable = new(() => Load("ar"));
    private static readonly Lazy<IReadOnlyList<string>> PluralBases = new(FindPluralBases);
    private static readonly ConcurrentDictionary<string, byte> ReportedMisses = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static Language _preferred = Language.System;
    private static volatile bool _strictMode;

    /// <summary>Raised after the language changes, so open windows can re-render.</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// The effective UI language: never <see cref="Language.System"/> and never <see cref="Language.Czech"/>.
    /// Assigning <see cref="Language.System"/> makes the getter follow <see cref="Detect"/>.
    /// </summary>
    public static Language Current
    {
        get => Resolve(_preferred);
        set
        {
            if (_preferred == value)
                return;
            _preferred = value;
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>The stored preference, which can still be <see cref="Language.System"/>.</summary>
    public static Language Preferred => _preferred;

    /// <summary>
    /// Number of strings in the active table. Used by the self-test and <c>--diagnose</c> to catch a trimmed
    /// build that failed to load the embedded tables (which would otherwise show keys like
    /// <c>settings.title</c> instead of text).
    /// </summary>
    public static int KeyCount => (Current == Language.Arabic ? ArabicTable : EnglishTable).Value.Count;

    /// <summary>Arabic for <c>ar-*</c> UI cultures, English everywhere else.</summary>
    public static Language Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase)
            ? Language.Arabic
            : Language.English;

    /// <summary>True when the current language is written right-to-left (Arabic).</summary>
    public static bool IsRtl => Current == Language.Arabic;

    /// <summary>
    /// When true, a missing key throws <see cref="MissingLocalizationKeyException"/> instead of returning
    /// the key. Tests enable it; the app leaves it off.
    /// </summary>
    public static bool StrictMode
    {
        get => _strictMode;
        set => _strictMode = value;
    }

    /// <summary>Keys that were missing at runtime, in the form "en:key" / "ar:key".</summary>
    public static IReadOnlyCollection<string> MissingKeys => ReportedMisses.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>Base keys of plural groups, e.g. <c>items.count</c>; each group has CLDR category suffixes.</summary>
    public static IReadOnlyList<string> PluralBaseKeys => PluralBases.Value;

    /// <summary>The string for <paramref name="key"/> in the current language.</summary>
    public static string T(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var language = Current;
        if (TableFor(language).TryGetValue(key, out var value))
            return value;
        ReportMissing(key, language);
        return key;
    }

    /// <summary>
    /// The string for <paramref name="key"/> with <c>{0}</c>/<c>{1}</c>… replaced by <paramref name="args"/>.
    /// Also accepts the legacy <c>T(czech, english)</c> call shape and returns the English text for it.
    /// </summary>
    [OverloadResolutionPriority(1)]
    public static string T(string key, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var language = Current;
        if (TableFor(language).TryGetValue(key, out var template))
            return Format(template, args, key);

        // Legacy T(czech, english): an unknown key that carries exactly one string argument.
        // Strict mode still fails, so tests cannot hide a genuinely missing key behind this path.
        if (!StrictMode && args.Length == 1 && args[0] is string english)
            return english;

        ReportMissing(key, language);
        return key;
    }

    /// <summary>
    /// The old two-language API. It is no longer used by new code; it stays so that existing callers keep
    /// compiling (and keeps returning English) until the app's call sites are migrated.
    /// </summary>
    [Obsolete("Use T(\"key\") with the embedded tables. This overload returns English and is removed at integration.")]
    public static string T(string czech, string english) => english;

    /// <summary>
    /// The correct plural form for <paramref name="n"/>: English picks one/other, Arabic picks
    /// zero/one/two/few/many/other (CLDR rules). <paramref name="key"/> is the base key, e.g. <c>items.count</c>.
    /// </summary>
    public static string Plural(string key, int n)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var language = Current;
        var category = Category(language, Math.Abs(n));
        if (TableFor(language).TryGetValue($"{key}.{category}", out var value))
            return Format(value, [n], $"{key}.{category}");

        // A translation may omit a category; "other" is the CLDR-recommended fallback.
        if (category != "other" && TableFor(language).TryGetValue($"{key}.other", out var other))
        {
            ReportMissing($"{key}.{category}", language);
            return Format(other, [n], $"{key}.other");
        }

        ReportMissing($"{key}.{category}", language);
        return $"{key}.{category}";
    }

    /// <summary>"1 item", "3 items"; Arabic uses the right form for 0/1/2/3–10/11–99/100+.</summary>
    public static string Items(int n) => Plural("items.count", n);

    /// <summary>All keys of one concrete language table (English or Arabic).</summary>
    public static IReadOnlyDictionary<string, string> Table(Language language) => TableFor(Resolve(language));

    /// <summary>True when <paramref name="key"/> exists in the given language's table.</summary>
    public static bool HasKey(string key, Language language) =>
        !string.IsNullOrEmpty(key) && TableFor(Resolve(language)).ContainsKey(key);

    private static Language Resolve(Language language) => language switch
    {
        Language.Arabic => Language.Arabic,
        Language.English => Language.English,
        Language.Czech => Language.English,
        _ => Detect(),
    };

    private static IReadOnlyDictionary<string, string> TableFor(Language language) =>
        language == Language.Arabic ? ArabicTable.Value : EnglishTable.Value;

    private static IReadOnlyDictionary<string, string> Load(string code)
    {
        var name = ResourcePrefix + code + ".json";
        try
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream(name);
            if (stream is null)
                throw new InvalidOperationException($"Embedded string table '{name}' was not found.");

            // JsonDocument is trim-safe. The previous reflection-based dictionary deserialization
            // depends on metadata that a trimmed self-contained publish can remove: every lookup
            // then fell back to the key (the black 2.2.0 Settings window was titled "settings.title").
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException($"Embedded string table '{name}' is not a JSON object.");

            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    table[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            return table;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // A broken build should not take the UI down; every lookup then falls back to the key.
            Debug.WriteLine($"[Loc] Could not load '{name}': {ex.Message}");
            return Empty;
        }
    }

    private static IReadOnlyList<string> FindPluralBases() =>
        EnglishTable.Value.Keys
            .Where(k => k.EndsWith(".other", StringComparison.Ordinal))
            .Select(k => k[..^".other".Length])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Category(Language language, int n)
    {
        if (language != Language.Arabic)
            return n == 1 ? "one" : "other";
        return n switch
        {
            0 => "zero",
            1 => "one",
            2 => "two",
            _ when n % 100 is >= 3 and <= 10 => "few",
            _ when n % 100 is >= 11 and <= 99 => "many",
            _ => "other",
        };
    }

    private static string Format(string template, object?[] args, string key)
    {
        if (args.Length == 0)
            return template;
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            ReportFormatError(key);
            return template;
        }
    }

    private static void ReportMissing(string key, Language language)
    {
        if (StrictMode)
            throw new MissingLocalizationKeyException(key, language);
        if (ReportedMisses.TryAdd($"{language.ToString().ToLowerInvariant()}:{key}", 0))
            Debug.WriteLine($"[Loc] Missing string '{key}' for {language}; showing the key.");
    }

    private static void ReportFormatError(string key)
    {
        if (ReportedMisses.TryAdd($"format:{key}", 0))
            Debug.WriteLine($"[Loc] Bad placeholders in '{key}'; showing the template.");
    }
}
