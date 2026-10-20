using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FileAction.App;
using FileAction.App.UI;
using FileAction.App.UI.Tools.Image;
using FileAction.Core;
using ImageMagick;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Real output for the Redact tool: the flatten plan and the Magick writer are the same code the window
/// uses, so these cases produce actual files and read them back. They pin the promises the UI makes —
/// a solid area is exactly the chosen colour, a blur visibly changes only its own area, the source file is
/// byte-identical afterwards, the copy gets a unique "Redacted" name, metadata and thumbnails are gone, and
/// the OCR path degrades to "unavailable" instead of throwing.
/// </summary>
public class RedactOutputTests : IDisposable
{
    private const int SourceWidth = 240;
    private const int SourceHeight = 160;

    private readonly string _directory;

    public RedactOutputTests()
    {
        _directory = OutputPaths.TempDirectory();
    }

    public void Dispose() => OutputPaths.TryDelete(_directory);

    // ---------------------------------------------------------------- real output

    [Fact]
    public void Redact_apply_fills_the_area_with_the_chosen_solid_colour()
    {
        var source = CreateSource();
        var area = new RedactionBox(40, 30, 60, 40);
        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(area)],
            new RedactionStyle(RedactionMode.Solid, 25, RedactionColors.Black));

        var output = RedactRunner.Apply(new FileItem(source), plan, _directory);

        using var image = new MagickImage(output);
        Assert.Equal((uint)SourceWidth, image.Width);
        Assert.Equal((uint)SourceHeight, image.Height);

        using var patch = new MagickImage(new MagickColor(0, 0, 0), 60, 40);
        using var region = new MagickImage(image.CloneArea(40, 30, 60, 40));
        Assert.Equal(0d, region.Compare(patch, ErrorMetric.RootMeanSquared), 6);

        // Everything outside the area is still the source's noise, not a flattened fill.
        using var sourceImage = new MagickImage(source);
        using var untouchedOutput = new MagickImage(image.CloneArea(0, 0, SourceWidth, 20));
        using var untouchedSource = new MagickImage(sourceImage.CloneArea(0, 0, SourceWidth, 20));
        Assert.Equal(0d, untouchedOutput.Compare(untouchedSource, ErrorMetric.RootMeanSquared), 6);
    }

    [Fact]
    public void Redact_apply_blurs_only_the_area()
    {
        var source = CreateSource();
        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(new RedactionBox(60, 40, 80, 60))],
            new RedactionStyle(RedactionMode.Blur, 40, RedactionColors.Black));

        var output = RedactRunner.Apply(new FileItem(source), plan, _directory);

        using var image = new MagickImage(output);
        using var sourceImage = new MagickImage(source);
        using var blurred = new MagickImage(image.CloneArea(60, 40, 80, 60));
        using var original = new MagickImage(sourceImage.CloneArea(60, 40, 80, 60));
        using var untouchedOutput = new MagickImage(image.CloneArea(0, 120, SourceWidth, 40));
        using var untouchedSource = new MagickImage(sourceImage.CloneArea(0, 120, SourceWidth, 40));

        // The noise is smoothed, so the region must differ from the source; the strip below is untouched.
        Assert.True(
            blurred.Compare(original, ErrorMetric.RootMeanSquared) > 0.001,
            "the blurred area is identical to the source");
        Assert.Equal(0d, untouchedOutput.Compare(untouchedSource, ErrorMetric.RootMeanSquared), 6);
    }

    [Fact]
    public void Redact_apply_pixelates_only_the_area()
    {
        var source = CreateSource();
        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(new RedactionBox(20, 20, 80, 60))],
            new RedactionStyle(RedactionMode.Pixelate, 45, RedactionColors.Black));

        var output = RedactRunner.Apply(new FileItem(source), plan, _directory);

        using var image = new MagickImage(output);
        using var sourceImage = new MagickImage(source);
        using var pixelated = new MagickImage(image.CloneArea(20, 20, 80, 60));
        using var original = new MagickImage(sourceImage.CloneArea(20, 20, 80, 60));

        Assert.True(
            pixelated.Compare(original, ErrorMetric.RootMeanSquared) > 0.001,
            "the pixelated area is identical to the source");
        // Pixelation collapses detail: fewer distinct colours than the noise it replaced.
        Assert.True(pixelated.TotalColors < original.TotalColors, "pixelation did not reduce detail");
    }

    [Fact]
    public void Redact_apply_leaves_the_source_byte_identical()
    {
        var source = CreateSource();
        var before = Sha256(source);
        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(new RedactionBox(10, 10, 50, 50))],
            RedactionStyle.Default);

        _ = RedactRunner.Apply(new FileItem(source), plan, _directory);

        Assert.Equal(before, Sha256(source));
        Assert.True(File.Exists(source), "the source file disappeared");
    }

    [Fact]
    public void Redact_apply_writes_a_unique_name_and_never_overwrites()
    {
        var source = CreateSource();
        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(new RedactionBox(10, 10, 50, 50))],
            RedactionStyle.Default);

        var first = RedactRunner.Apply(new FileItem(source), plan, _directory);
        var firstHash = Sha256(first);
        var second = RedactRunner.Apply(new FileItem(source), plan, _directory);

        Assert.Equal("photo Redacted.png", Path.GetFileName(first));
        Assert.Equal("photo Redacted (2).png", Path.GetFileName(second));
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first), "the first copy disappeared");
        Assert.True(File.Exists(second), "the second copy was not written");
        Assert.Equal(firstHash, Sha256(first));
    }

    [Fact]
    public void Redact_apply_strips_metadata_and_embedded_profiles()
    {
        var source = CreateSource();

        // The fixture really carries metadata for the tool to remove (PNG keeps the comment; the XMP
        // profile is asserted too when the writer round-trips it).
        bool hadMetadata;
        using (var original = new MagickImage(source))
        {
            hadMetadata = !string.IsNullOrEmpty(original.Comment) || original.GetProfile("xmp") is not null;
        }
        Assert.True(hadMetadata, "the fixture carries no metadata to strip");

        var plan = RedactionPlan.Build(
            SourceWidth, SourceHeight, [new RedactionArea(new RedactionBox(10, 10, 50, 50))],
            RedactionStyle.Default);
        var output = RedactRunner.Apply(new FileItem(source), plan, _directory);

        using var image = new MagickImage(output);
        Assert.True(string.IsNullOrEmpty(image.Comment), $"output still carries the comment: {image.Comment}");
        Assert.Null(image.GetProfile("xmp"));
        Assert.Null(image.GetProfile("exif"));
        Assert.Null(image.GetProfile("icc"));
    }

    [Fact]
    public void Redact_text_detection_reports_unavailable_instead_of_throwing()
    {
        var source = CreateSource();

        // Real image: either OCR is unavailable (null) or it returns usable, non-empty boxes — never a
        // crash, and never an empty box that would redact nothing.
        IReadOnlyList<RedactionBox>? boxes = null;
        var exception = Record.Exception(() => boxes = RedactTextDetector.Detect(File.ReadAllBytes(source), CancellationToken.None));
        Assert.Null(exception);
        if (boxes is not null)
            Assert.All(boxes, box => Assert.False(box.IsEmpty));

        // Not an image at all: the same graceful "unavailable", not an exception.
        Assert.Null(RedactTextDetector.Detect([0xDE, 0xAD, 0xBE, 0xEF], CancellationToken.None));
    }

    // ---------------------------------------------------------------- theme and label

    [Fact]
    public void Theme_switch_changes_the_palette_the_tool_uses()
    {
        var previous = Theme.Override;
        try
        {
            Theme.Apply(ThemeMode.Dark);
            Assert.True(Theme.IsDark);
            var darkText = ColorOf(Theme.Text);
            var darkCard = ColorOf(Theme.Card);
            var darkMuted = ColorOf(Theme.Muted);

            Theme.Apply(ThemeMode.Light);
            Assert.False(Theme.IsDark);
            Assert.NotEqual(darkText, ColorOf(Theme.Text));
            Assert.NotEqual(darkCard, ColorOf(Theme.Card));
            Assert.NotEqual(darkMuted, ColorOf(Theme.Muted));
        }
        finally
        {
            Theme.Override = previous;
            Theme.Refresh();
        }
    }

    [Fact]
    public void Redact_view_paints_its_chrome_from_the_theme()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Image", "RedactView.cs"));

        // Only the user's swatch colours are literals; every chrome brush comes from the active palette.
        // ("RedactionColors." must not count as a hardcoded "Colors." brush, hence the word boundary.)
        Assert.False(Regex.IsMatch(source, @"\bColors\."), "a hardcoded Colors.* brush found; use Theme.* instead");
        foreach (Match brush in Regex.Matches(source, @"Brushes\.(\w+)"))
            Assert.Equal("Transparent", brush.Groups[1].Value);

        foreach (var themed in new[]
                 {
                     "Theme.Card", "Theme.Accent", "Theme.AccentGradient", "Theme.Muted",
                     "Theme.Text", "Theme.TextOnAccent", "Theme.Track", "Theme.PanelBorder",
                 })
        {
            Assert.Contains(themed, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Redact_primary_button_says_save_redacted_copy()
    {
        Assert.Equal("tool.redact.save", RedactTool.Instance.PrimaryLabelKey);

        var english = Loc.Table(Language.English);
        var arabic = Loc.Table(Language.Arabic);
        Assert.True(english.TryGetValue("tool.redact.save", out var label), "en.json has no tool.redact.save");
        Assert.Equal("Save Redacted Copy", label);
        Assert.True(arabic.ContainsKey("tool.redact.save"), "ar.json has no tool.redact.save");

        // Every string the tool owns exists in both tables (the button label is not a literal T(...) call).
        var required = new[]
        {
            "tool.redact.label", "tool.redact.title", "tool.redact.subtitle", "tool.redact.suffix",
            "tool.redact.save", "tool.redact.mode.solid", "tool.redact.mode.blur", "tool.redact.mode.pixelate",
            "tool.redact.strength", "tool.redact.color", "tool.redact.target.all", "tool.redact.target.selected",
            "tool.redact.detectText", "tool.redact.delete", "tool.redact.hint", "tool.redact.detecting",
            "tool.redact.noText", "tool.redact.textUnavailable", "tool.redact.noAreas", "tool.redact.notReady",
        };
        foreach (var key in required)
        {
            Assert.True(english.ContainsKey(key), $"en.json is missing {key}");
            Assert.True(arabic.ContainsKey(key), $"ar.json is missing {key}");
        }
        Assert.Equal(required.Length, english.Keys.Count(key => key.StartsWith("tool.redact.", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- helpers

    private static System.Windows.Media.Color ColorOf(System.Windows.Media.Brush brush) =>
        ((System.Windows.Media.SolidColorBrush)brush).Color;

    /// <summary>A noisy fixture (so blur/pixelate have detail to destroy) with comment and XMP profile.</summary>
    private string CreateSource(string name = "photo.png")
    {
        using var image = new MagickImage(new MagickColor(0x33, 0x66, 0xCC), SourceWidth, SourceHeight);
        image.AddNoise(NoiseType.Random);
        image.Comment = "private note";
        image.SetProfile(new ImageProfile("xmp", "<x:xmpmeta>secret</x:xmpmeta>"u8.ToArray()));

        var path = Path.Combine(_directory, name);
        image.Write(path);
        return path;
    }

    private static string Sha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
