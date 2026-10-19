using System.Globalization;

namespace FileAction.Core.Updates;

/// <summary>
/// Numeric version parsing/comparison for release tags. Never compares version strings: 2.10.0 &gt; 2.9.0.
/// Accepts an optional leading <c>v</c>, two to four numeric parts, and ignores SemVer build metadata.
/// Prereleases (<c>2.3.0-beta.1</c>) are rejected by <see cref="IsStable"/> and never offered as updates.
/// </summary>
public static class SemanticVersion
{
    /// <summary>Parses <c>v2.2.0</c>, <c>2.2</c>, <c>2.2.0.1</c>. Returns false for prereleases and junk.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        if (value.Contains('-', StringComparison.Ordinal))
            return false; // prerelease: ignored by the updater

        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4)
            return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>True when the tag is a stable release (no <c>-</c> prerelease suffix) and parses.</summary>
    public static bool IsStable(string? tag) => TryParse(tag, out _);

    /// <summary>Negative when <paramref name="left"/> is older, zero when equal, positive when newer.</summary>
    public static int Compare(Version left, Version right) => left.CompareTo(right);

    /// <summary>True when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(Version candidate, Version current) => Compare(candidate, current) > 0;
}
