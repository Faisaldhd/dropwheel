namespace FileAction.Core.Updates;

/// <summary>
/// Reads the release's <c>SHA256SUMS.txt</c> (<c>hash  filename</c> per line, sha256sum format) and checks one
/// file against it. Used together with the GitHub asset <c>digest</c> field as a second, independent digest source.
/// </summary>
public static class Sha256Sums
{
    /// <summary>The lower-case hash listed for <paramref name="fileName"/>, or null when the file is not listed.</summary>
    public static string? Find(string? text, string fileName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length < 66)
                continue;
            var hash = line[..64];
            if (!IsHex(hash))
                continue;
            var rest = line[64..].TrimStart();
            if (rest.StartsWith('*'))
                rest = rest[1..];
            if (string.Equals(rest.Trim(), fileName, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>True when the sums text lists <paramref name="fileName"/> with exactly <paramref name="actualHex"/>.</summary>
    public static bool Matches(string? text, string fileName, string? actualHex) =>
        !string.IsNullOrWhiteSpace(actualHex) &&
        string.Equals(Find(text, fileName), actualHex.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }
        return true;
    }
}
