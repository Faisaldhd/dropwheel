using System.Text.RegularExpressions;

namespace FileAction.Tests;

/// <summary>
/// R-15 acceptance: shipped sources carry no Czech UI strings; every user-visible string comes from
/// <c>Loc.T("key")</c>. The check is intentionally simple and auditable, with these exact rules:
/// <list type="number">
/// <item>Scan every <c>.cs</c> file under <c>src</c>, skipping <c>src/FileAction.Linux/**</c> (not shipped).</item>
/// <item>Ignore comments with a per-line cut: drop everything from <c>//</c>, drop inline <c>/* ... */</c>,
/// and skip lines whose trimmed start is <c>*</c> or <c>/*</c>.</item>
/// <item>Scan string literals: regular <c>"..."</c> (with escapes), verbatim <c>@"..."</c>, and triple-quoted
/// raw blocks such as <c>$"""..."""</c>. Raw blocks are matched across lines because their text has no quotes.</item>
/// <item>Exempt fixture paths: a single-line literal containing <c>\</c> or <c>/</c>, or ending in
/// <c>.jpg</c>, <c>.png</c>, <c>.mov</c>, <c>.pdf</c>, <c>.docx</c>, <c>.xlsx</c> or <c>.zip</c>, is a demo
/// file name rather than UI text.</item>
/// <item>Any remaining literal with a Czech letter fails with the file and line and a <c>Loc.T("key")</c> hint;
/// repeated <c>file:line</c> locations are reported once.</item>
/// </list>
/// Consequence: Czech developer comments and Czech demo file names stay legal; only user-facing text is
/// enforced. This is not a C# parser, so an unusual construct in a comment or literal could be misread.
/// </summary>
public class NoCzechTests
{
    /// <summary>Czech letters (lower and upper case) that must not appear in shipped UI strings.</summary>
    private static readonly char[] CzechDiacritics = "ěščřžýáíéůúťďňĚŠČŘŽÝÁÍÉŮÚŤĎŇ".ToCharArray();

    /// <summary>Verbatim or regular string literal; verbatim comes first so doubled quotes stay together.</summary>
    private static readonly Regex StringLiteral = new(@"@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    /// <summary>A triple-quoted raw string block, including its multi-line content.</summary>
    private static readonly Regex RawString = new("\"\"\"(.*?)\"\"\"", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>An inline block comment on an otherwise code-bearing line.</summary>
    private static readonly Regex InlineBlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled);

    private static readonly string[] FixtureExtensions = [".jpg", ".png", ".mov", ".pdf", ".docx", ".xlsx", ".zip"];

    [Fact]
    public void Shipped_sources_contain_no_czech_ui_text()
    {
        var root = RepoRoot();
        var src = Path.Combine(root, "src");
        var skipLinux = Directory.Exists(Path.Combine(src, "FileAction.Linux"));

        var violations = new List<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;

        void Report(string file, int line)
        {
            var message = $"Czech UI text left in {file}:{line}; use Loc.T(\"key\")";
            if (reported.Add(message))
                violations.Add(message);
        }

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relativeToSrc = Path.GetRelativePath(src, file);

            // FileAction.Linux is a separate, unshipped target; its sources are out of scope.
            if (skipLinux && relativeToSrc.StartsWith("FileAction.Linux" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            scanned++;
            var relative = Path.GetRelativePath(root, file);
            var text = File.ReadAllText(file);

            // Raw string blocks span lines, so find their content in the whole file first.
            foreach (Match block in RawString.Matches(text))
            {
                var content = block.Groups[1];
                var czech = content.Value.IndexOfAny(CzechDiacritics);
                if (czech < 0)
                    continue;
                var line = text[..(content.Index + czech)].Count(character => character == '\n') + 1;
                Report(relative, line);
            }

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComments(lines[i]);
                foreach (Match match in StringLiteral.Matches(code))
                {
                    if (IsFixturePath(match.Value))
                        continue;
                    if (match.Value.IndexOfAny(CzechDiacritics) >= 0)
                    {
                        Report(relative, i + 1); // One actionable location per line.
                        break;
                    }
                }
            }
        }

        Assert.True(scanned > 0, $"no .cs files found under {src}");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Simple per-line comment removal; see the class doc for the exact limits.</summary>
    private static string StripComments(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
            return string.Empty;

        var lineComment = line.IndexOf("//", StringComparison.Ordinal);
        if (lineComment >= 0)
            line = line[..lineComment];

        return InlineBlockComment.Replace(line, string.Empty);
    }

    /// <summary>True for demo path/file literals such as <c>@"C:\Fotky\dovolená.jpg"</c>.</summary>
    private static bool IsFixturePath(string literal)
    {
        var text = literal.TrimStart('@').Trim('"');
        if (text.Contains('\\') || text.Contains('/'))
            return true;
        return FixtureExtensions.Any(extension => text.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
