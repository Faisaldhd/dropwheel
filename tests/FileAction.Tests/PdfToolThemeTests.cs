using System.Text.RegularExpressions;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The PDF tool windows must follow the shared purple theme: every colour comes from <c>Theme</c>
/// (a static scan of the tool folder), and an open window rebuilds itself when Windows switches between
/// the light and dark app theme.
/// </summary>
public class PdfToolThemeTests
{
    private static readonly Regex[] HardCodedColours =
    [
        new(@"\bColor\.From", RegexOptions.Compiled),
        new(@"\bColors\.", RegexOptions.Compiled),
        new(@"#[0-9A-Fa-f]{6}\b", RegexOptions.Compiled),
        new(@"\bSolidColorBrush\s*\(", RegexOptions.Compiled),
        // Brushes.Transparent is the one allowed brush: WPF only hit-tests a transparent surface.
        new(@"\bBrushes\.(?!Transparent\b)", RegexOptions.Compiled),
    ];

    private static string Folder() =>
        Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Pdf");

    [Fact]
    public void Pdf_tool_views_have_no_hard_coded_colours()
    {
        var files = Directory.GetFiles(Folder(), "*.cs");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in HardCodedColours)
                foreach (Match match in pattern.Matches(text))
                    violations.Add($"{Path.GetFileName(file)}:{LineOf(text, match.Index)}: {match.Value}");

            // The operation classes build no visuals; every view and helper must use Theme brushes.
            if (!string.Equals(Path.GetFileName(file), "PdfToolOperations.cs", StringComparison.Ordinal)
                && !text.Contains("Theme.", StringComparison.Ordinal))
            {
                violations.Add($"{Path.GetFileName(file)}: no Theme brush is used");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hard-coded colours or a missing Theme brush in the PDF tools:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("PdfMergeTool.cs")]
    [InlineData("PdfOrganizeTool.cs")]
    public void Pdf_tool_views_repaint_when_the_theme_changes(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(Folder(), fileName));

        Assert.Contains("PdfThemeWatcher.Subscribe", text, StringComparison.Ordinal);
        Assert.Contains("BuildBody()", text, StringComparison.Ordinal);
    }

    private static int LineOf(string text, int index) =>
        text[..index].Count(character => character == '\n') + 1;

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
