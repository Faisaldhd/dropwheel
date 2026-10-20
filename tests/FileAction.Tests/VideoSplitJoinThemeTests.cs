using System.Text.RegularExpressions;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Theme and labelling rules for the Split and Join tools: their sources use <c>Theme</c> brushes only
/// (no colour literals, no hand-built <c>SolidColorBrush</c>), they rebuild themselves when the palette
/// changes, and their primary button carries the tool's own verb instead of "Apply".
/// </summary>
public class VideoSplitJoinThemeTests
{
    /// <summary>
    /// Colour-producing APIs that would bypass the palette. <c>Brushes.Transparent</c> is allowed: it is
    /// the hit-test neutral for the timeline's drag handles, not a colour choice.
    /// </summary>
    private static readonly Regex[] HardCodedColours =
    [
        new(@"Color\.From(Argb|Rgb|ScRgb|Values)\s*\(", RegexOptions.Compiled),
        new(@"\bColors\.[A-Z][A-Za-z]*", RegexOptions.Compiled),
        new(@"\bBrushes\.(?!Transparent\b)[A-Z][A-Za-z]*", RegexOptions.Compiled),
        new(@"new\s+SolidColorBrush\s*\(", RegexOptions.Compiled),
        new(@"""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled),
    ];

    [Fact]
    public void The_theme_scan_finds_the_video_tool_sources()
    {
        var files = VideoToolSources();

        Assert.Equal(5, files.Count);
        Assert.Contains(files, file => file.EndsWith("VideoSplitTool.cs", StringComparison.Ordinal));
        Assert.Contains(files, file => file.EndsWith("VideoJoinTool.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_video_tools_use_theme_brushes_only()
    {
        var hits = new List<string>();
        foreach (var file in VideoToolSources())
        {
            var lines = File.ReadAllLines(file);
            foreach (var pattern in HardCodedColours)
            {
                for (var index = 0; index < lines.Length; index++)
                {
                    if (pattern.IsMatch(lines[index]))
                        hits.Add($"{Path.GetFileName(file)}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(
            hits.Count == 0,
            "Hard-coded colours in the video tools; use Theme brushes instead:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", hits));
    }

    [Fact]
    public void Both_views_rebuild_their_palette_when_the_theme_changes()
    {
        var sources = VideoToolSources().ToDictionary(Path.GetFileName, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

        foreach (var view in new[] { "VideoSplitTool.cs", "VideoJoinTool.cs" })
        {
            Assert.Contains("new ThemeWatcher(", sources[view]);
            Assert.Contains("ThemeWatcher", sources[view]);
        }

        // The watcher itself must refresh the palette and listen to both change sources.
        var common = sources["VideoToolCommon.cs"];
        Assert.Contains("Theme.Refresh()", common);
        Assert.Contains("Theme.IsDark", common);
        Assert.Contains("SystemEvents.UserPreferenceChanged", common);
        Assert.Contains("_window.Activated", common);
    }

    [Fact]
    public void The_primary_button_labels_are_the_tool_verbs()
    {
        Assert.Equal("tool.videosplit.primary", VideoSplitTool.Instance.PrimaryLabelKey);
        Assert.Equal("tool.videojoin.primary", VideoJoinTool.Instance.PrimaryLabelKey);

        Assert.Equal("Split Video", Loc.T("tool.videosplit.primary"));
        Assert.Equal("Join Videos", Loc.T("tool.videojoin.primary"));

        // Both strings are localized: Arabic must have its own text, not the English verb.
        var arabic = Loc.Table(Language.Arabic);
        Assert.True(arabic.ContainsKey("tool.videosplit.primary"));
        Assert.True(arabic.ContainsKey("tool.videojoin.primary"));
        Assert.NotEqual("Split Video", arabic["tool.videosplit.primary"]);
        Assert.NotEqual("Join Videos", arabic["tool.videojoin.primary"]);
    }

    private static readonly string[] SplitJoinSourceNames =
    [
        "VideoSplitTool.cs",
        "VideoJoinTool.cs",
        "VideoSplitRunners.cs",
        "VideoToolCommon.cs",
        "VideoSplitJoinOperations.cs",
    ];

    private static List<string> VideoToolSources()
    {
        var directory = Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Video");
        Assert.True(Directory.Exists(directory), $"missing folder: {directory}");
        return [.. SplitJoinSourceNames
            .Select(name => Path.Combine(directory, name))
            .OrderBy(path => path, StringComparer.Ordinal)];
    }
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}