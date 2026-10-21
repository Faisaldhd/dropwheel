using System.Globalization;
using System.Text.RegularExpressions;

namespace FileAction.Core;

/// <summary>
/// Which pages one PDF contributes to a merge: every page when the typed range is empty, otherwise the
/// pages named by a list such as "1-3, 5". The list is resolved in document order, so repeated or
/// out-of-order numbers cannot duplicate or reorder pages. Parsing is pure and does not know the document,
/// so the window can flag a typo while the real page count is checked separately.
/// </summary>
public sealed class PdfPageSelection
{
    /// <summary>A span larger than this cannot be a real document; it also keeps a typo from looping long.</summary>
    private const int MaxPagesPerSpan = 100_000;

    private static readonly char[] Separators = [',', ';', '\u060C', ' ', '\t', '\r', '\n'];

    private static readonly Regex SpacedDash = new(@"\s*-\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The empty range: every page of the document.</summary>
    public static readonly PdfPageSelection All = new([]);

    private readonly int[] _pages;

    private PdfPageSelection(int[] pages) => _pages = pages;

    /// <summary>True when the range is empty and the whole document is used.</summary>
    public bool IsAll => _pages.Length == 0;

    /// <summary>The 0-based pages in ascending document order; empty when <see cref="IsAll"/>.</summary>
    public IReadOnlyList<int> Pages => _pages;

    /// <summary>
    /// Reads a typed list such as "1-3, 5-7". Empty or whitespace means every page. Commas, semicolons,
    /// the Arabic comma and whitespace separate entries; an en/em dash or a minus sign counts as a hyphen.
    /// </summary>
    public static bool TryParse(string? text, out PdfPageSelection selection)
    {
        selection = All;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = SpacedDash.Replace(
            text.Trim().Replace('\u2013', '-').Replace('\u2014', '-').Replace('\u2212', '-'),
            "-");
        var tokens = normalized.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        var pages = new SortedSet<int>();
        foreach (var token in tokens)
        {
            var dash = token.IndexOf('-');
            if (dash < 0)
            {
                if (!TryPage(token, out var page))
                    return false;
                pages.Add(page);
                continue;
            }

            // Exactly one hyphen: "1-3" is a span, "1--3" and "1-2-3" are typos.
            if (dash != token.LastIndexOf('-'))
                return false;
            if (!TryPage(token[..dash], out var first) || !TryPage(token[(dash + 1)..], out var last))
                return false;
            if (last < first || last - first >= MaxPagesPerSpan)
                return false;
            for (var page = first; page <= last; page++)
                pages.Add(page);
        }

        selection = new PdfPageSelection([.. pages.Select(page => page - 1)]);
        return true;
    }

    /// <summary>
    /// True when every named page exists in a document of <paramref name="pageCount"/> pages. This is the
    /// cheap validation used while the user types; it never materializes the page list.
    /// </summary>
    public bool Fits(int pageCount) => pageCount >= 0 && (IsAll || _pages[^1] < pageCount);

    /// <summary>
    /// Resolves the selection against the real page count. False means a named page is not in the
    /// document; <paramref name="pages"/> is then empty.
    /// </summary>
    public bool TryResolve(int pageCount, out IReadOnlyList<int> pages)
    {
        if (!Fits(pageCount))
        {
            pages = [];
            return false;
        }

        pages = IsAll ? Enumerable.Range(0, pageCount).ToArray() : _pages;
        return true;
    }

    /// <summary>A 1-based page number: digits only, at least one, short enough not to overflow.</summary>
    private static bool TryPage(string text, out int page)
    {
        page = 0;
        if (text.Length is 0 or > 9)
            return false;
        foreach (var character in text)
        {
            if (character is < '0' or > '9')
                return false;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;
    }
}
