using System.Globalization;

namespace FileAction.App.UI;

/// <summary>
/// Unicode bidirectional isolates for dynamic values. A Latin file name, size, percentage, time or
/// format dropped into an Arabic sentence can otherwise be reordered by the bidi algorithm; an isolate
/// (U+2066 LRI … U+2069 PDI) keeps the value as one left-to-right run.
/// </summary>
internal static class Bidi
{
    private const char LeftToRightIsolate = '\u2066';
    private const char PopDirectionalIsolate = '\u2069';

    /// <summary>
    /// Wraps a technical value for RTL text. In LTR text the value is returned unchanged, so the
    /// helper can be used at every call site without a condition.
    /// </summary>
    public static string Value(string? text)
    {
        if (string.IsNullOrEmpty(text) || !LanguageManager.IsRtl)
            return text ?? string.Empty;
        return $"{LeftToRightIsolate}{text}{PopDirectionalIsolate}";
    }

    /// <summary>A whole number with Western digits, isolated the same way.</summary>
    public static string Number(int value) => Value(value.ToString(CultureInfo.InvariantCulture));
}
