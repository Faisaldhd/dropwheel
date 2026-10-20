using System.Text.RegularExpressions;
using FileAction.App.UI.Tools.Video;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// Wave B guard rails for the video tools: no literal UI colours outside the content palette, a theme
/// hook in both windows, and the state-dependent primary labels. The source scan is deliberately simple
/// and auditable, in the same spirit as NoCzechTests.
/// </summary>
[Collection(nameof(LanguageSwitching))]
public class VideoToolThemeTests
{
    private static readonly Regex HardCodedColor = new(@"Color\.From|Colors\.|Brushes\.", RegexOptions.Compiled);

    private static string VideoFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
        return Path.Combine(root, "src", "FileAction.App", "UI", "Tools", "Video");
    }

    [Fact]
    public void Video_tools_use_theme_brushes_only()
    {
        var folder = VideoFolder();
        Assert.True(Directory.Exists(folder), $"missing tool folder: {folder}");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
        {
            // VideoPalette is the one documented home of the content colours (the redaction swatches).
            if (string.Equals(Path.GetFileName(file), "VideoPalette.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (HardCodedColor.IsMatch(line))
                    violations.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Hard-coded colours found outside VideoPalette.cs; use Theme brushes:"
            + Environment.NewLine
            + "  "
            + string.Join(Environment.NewLine + "  ", violations));
    }

    [Fact]
    public void Video_tools_repaint_when_the_theme_changes()
    {
        var folder = VideoFolder();
        var common = File.ReadAllText(Path.Combine(folder, "VideoCommon.cs"));
        Assert.Contains("class VideoThemeHook", common, StringComparison.Ordinal);
        Assert.Contains("SystemEvents.UserPreferenceChanged", common, StringComparison.Ordinal);

        foreach (var name in new[] { "VideoFramesTool.cs", "VideoRedactTool.cs" })
        {
            var source = File.ReadAllText(Path.Combine(folder, name));
            Assert.Contains("new VideoThemeHook(root, RepaintTheme)", source, StringComparison.Ordinal);
            Assert.Contains("private void RepaintTheme()", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Both_primary_labels_are_localized()
    {
        var previous = Loc.Current;
        try
        {
            Loc.Current = Language.English;
            Assert.Equal("Save Frames", Loc.T("tool.videoframes.primary.initial"));
            Assert.Equal("Save 0 Frames", Loc.T("tool.videoframes.primary.plural", 0));
            Assert.Equal("Save 1 Frame", Loc.T("tool.videoframes.primary.single", 1));
            Assert.Equal("Save Redacted Copy", Loc.T("tool.videoredact.primary"));
        }
        finally
        {
            Loc.Current = previous;
        }

        Assert.Equal("tool.videoframes.primary.initial", VideoFramesTool.Instance.PrimaryLabelKey);
        Assert.Equal("tool.videoredact.primary", VideoRedactTool.Instance.PrimaryLabelKey);

        foreach (var key in new[]
                 {
                     "tool.videoframes.primary.initial",
                     "tool.videoframes.primary.single",
                     "tool.videoframes.primary.plural",
                     "tool.videoredact.primary",
                 })
        {
            Assert.True(Loc.HasKey(key, Language.English), $"en.json is missing '{key}'");
            Assert.True(Loc.HasKey(key, Language.Arabic), $"ar.json is missing '{key}'");
        }
    }
}
