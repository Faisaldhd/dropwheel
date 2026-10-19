using System.Text.RegularExpressions;
using System.Windows.Media;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Guards the visual rules for the video tools: they may use <see cref="Theme"/> brushes only (plus the
/// intentional black/white content overlays), they must hook the theme-repaint path, and the shared
/// primary button label stays "Apply".
/// </summary>
public class VideoThemeTests
{
    /// <summary>Content colours that are deliberately literal: dimming, playhead and slider thumb.</summary>
    private static readonly HashSet<string> AllowedBrushes =
        new(StringComparer.Ordinal) { "Black", "White", "Transparent" };

    [Fact]
    public void Video_tool_sources_do_not_hard_code_theme_colours()
    {
        var folder = VideoFolder();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            var name = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var code = StripComment(lines[index]);
                var line = index + 1;

                foreach (Match brush in Regex.Matches(code, @"\bBrushes\.([A-Za-z]+)"))
                {
                    if (!AllowedBrushes.Contains(brush.Groups[1].Value))
                        violations.Add($"{name}:{line} {brush.Value} is not an allow-listed content colour");
                }

                if (Regex.IsMatch(code, @"\bColors\.[A-Za-z]"))
                    violations.Add($"{name}:{line} uses System.Windows.Media.Colors");
                if (Regex.IsMatch(code, @"\bColor\.From(Argb|Rgb|ScRgb|A)\b")
                    && !code.Contains("Color.FromArgb(0xE6, panel.R, panel.G, panel.B)", StringComparison.Ordinal))
                {
                    violations.Add($"{name}:{line} uses a literal Color.From*");
                }
                if (Regex.IsMatch(code, @"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b"))
                    violations.Add($"{name}:{line} contains a #RRGGBB literal");
                if (Regex.IsMatch(code, @"new SolidColorBrush\s*\(\s*(?:Color\.From|Colors\.)"))
                    violations.Add($"{name}:{line} builds a literal SolidColorBrush");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hard-coded colours in the video tool sources:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Video_views_hook_the_theme_repaint_path()
    {
        foreach (var view in new[] { "VideoTrimView.cs", "VideoCropView.cs", "VideoSpeedView.cs" })
        {
            var text = File.ReadAllText(Path.Combine(VideoFolder(), view));
            Assert.Contains("VideoThemeWatcher.Start();", text, StringComparison.Ordinal);
            Assert.Contains("VideoThemeTint.Apply(", text, StringComparison.Ordinal);
            Assert.Contains("VideoThemeWatcher.Changed -= OnThemeChanged", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Theme_mapping_replaces_palette_colours_and_keeps_content_colours()
    {
        var before = new VideoPalette(
            Text: Colors.Black,
            Muted: Colors.DimGray,
            Card: Colors.DarkSlateGray,
            PanelBorder: Colors.Gray,
            Track: Colors.SlateGray,
            AccentSoft: Colors.DarkBlue,
            Page: Colors.MidnightBlue,
            Panel: Colors.DarkOliveGreen,
            PanelGlass: Colors.DarkGreen);
        var after = new VideoPalette(
            Text: Colors.White,
            Muted: Colors.Gainsboro,
            Card: Colors.WhiteSmoke,
            PanelBorder: Colors.Silver,
            Track: Colors.LightGray,
            AccentSoft: Colors.LightBlue,
            Page: Colors.AliceBlue,
            Panel: Colors.LightYellow,
            PanelGlass: Colors.LightGreen);

        Assert.Equal(after.Text, VideoThemeTint.Map(before.Text, before, after));
        Assert.Equal(after.Muted, VideoThemeTint.Map(before.Muted, before, after));
        Assert.Equal(after.Card, VideoThemeTint.Map(before.Card, before, after));
        Assert.Equal(after.PanelBorder, VideoThemeTint.Map(before.PanelBorder, before, after));
        Assert.Equal(after.Track, VideoThemeTint.Map(before.Track, before, after));
        Assert.Equal(after.AccentSoft, VideoThemeTint.Map(before.AccentSoft, before, after));
        Assert.Equal(after.Page, VideoThemeTint.Map(before.Page, before, after));
        Assert.Equal(after.Panel, VideoThemeTint.Map(before.Panel, before, after));
        Assert.Equal(after.PanelGlass, VideoThemeTint.Map(before.PanelGlass, before, after));

        // Dimming / playhead / thumb: not palette members, so they stay exactly as built.
        Assert.Equal(Colors.Red, VideoThemeTint.Map(Colors.Red, before, after));
    }

    [Fact]
    public void The_shared_primary_button_label_is_Apply() =>
        Assert.Equal("Apply", Loc.Table(Language.English)["tool.window.apply"]);

    private static string StripComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }

    private static string VideoFolder()
    {
        var folder = Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Video");
        Assert.True(Directory.Exists(folder), $"missing video tool folder: {folder}");
        return folder;
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
