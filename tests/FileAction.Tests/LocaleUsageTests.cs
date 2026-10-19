using System.Text.RegularExpressions;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Every key the app and the core pass to <see cref="Loc.T(string)"/> must exist in both embedded tables.
/// The tables are hand-edited, so a call site that lands before its string, or a key renamed on one side,
/// would otherwise ship the raw key to the UI. This is the cross-check between source and
/// <c>src/FileAction.Core/Strings/*.json</c>.
/// </summary>
public class LocaleUsageTests
{
    /// <summary><c>Loc.T("key")</c>.</summary>
    private static readonly Regex QualifiedKeyCall = new(@"Loc\.T\(""([^""]+)""\)", RegexOptions.Compiled);

    /// <summary><c>T("key")</c> via <c>using static FileAction.Core.Loc;</c>.</summary>
    private static readonly Regex UnqualifiedKeyCall = new(@"T\(""([^""]+)""\)", RegexOptions.Compiled);

    // Each pattern demands the closing parenthesis immediately after the one quoted argument, so the old
    // two-language call shape T("česky", "English") can never be captured as a key. Any such legacy call site
    // that still exists is therefore ignored rather than reported as a missing key.
    private static readonly Regex[] KeyCallPatterns = [QualifiedKeyCall, UnqualifiedKeyCall];

    private static readonly string[] ScannedProjects = ["FileAction.App", "FileAction.Core"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }

    /// <summary>Every <c>.cs</c> file under the two scanned projects, excluding build output.</summary>
    private static IEnumerable<string> SourceFiles(string root)
    {
        foreach (var project in ScannedProjects)
        {
            var directory = Path.Combine(root, "src", project);
            Assert.True(Directory.Exists(directory), $"missing source folder: {directory}");

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var segments = Path.GetRelativePath(directory, file).Split(Path.DirectorySeparatorChar);
                if (segments.Any(segment => segment is "obj" or "bin"))
                    continue;
                yield return file;
            }
        }
    }

    /// <summary>All literal keys passed to a one-argument <c>T(...)</c> call, in ordinal order.</summary>
    private static SortedSet<string> UsedKeys(string root)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(root))
        {
            var source = File.ReadAllText(file);
            foreach (var pattern in KeyCallPatterns)
                foreach (Match match in pattern.Matches(source))
                    keys.Add(match.Groups[1].Value);
        }
        return keys;
    }

    [Fact]
    public void Every_key_used_in_source_exists_in_both_tables()
    {
        var keys = UsedKeys(RepoRoot());

        // A scan that silently finds nothing (wrong root, renamed projects) must not pass as success.
        Assert.NotEmpty(keys);

        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);

        var missing = keys
            .Where(key => !english.ContainsKey(key) || !arabic.ContainsKey(key))
            .Select(key => $"{key} (en: {english.ContainsKey(key)}, ar: {arabic.ContainsKey(key)})")
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Keys used in code but missing from Strings/en.json or Strings/ar.json:"
            + Environment.NewLine
            + "  "
            + string.Join(Environment.NewLine + "  ", missing));
    }
}
