using FileAction.App.UI;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The tool window shares Settings' frame geometry and gets its own anchored motion: the look is the
/// 20 DIP Settings card with a 12 DIP content card, the 18 DIP gutter and the dark violet halo, while
/// the open/close pop scales from the pointer that opened the wheel instead of the chrome centre.
/// </summary>
public class ToolWindowChromeTests
{
    [Fact]
    public void Frame_matches_the_settings_geometry_and_dark_halo()
    {
        Assert.Equal(20, Design.Radius.Window);
        Assert.Equal(12, Design.Radius.Card);
        Assert.Equal(18, Design.Gutter);

        var source = ReadToolWindow();
        Assert.Contains("CornerRadius = new CornerRadius(Design.Radius.Window)", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(Design.Radius.Card)", source, StringComparison.Ordinal);
        Assert.Contains("Padding = new Thickness(Design.Gutter, Design.Space.Md, Design.Gutter, Design.Space.Md)",
            source, StringComparison.Ordinal);
        Assert.Contains("private const double FrameMargin = 24;", source, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(FrameMargin)", source, StringComparison.Ordinal);
        Assert.Contains("Background = Theme.Panel", source, StringComparison.Ordinal);

        var shadow = Between(source, "private static System.Windows.Media.Effects.DropShadowEffect WindowShadow()",
            "/// <summary>Closes with the open pop reversed");
        Assert.Contains("BlurRadius = 38", shadow, StringComparison.Ordinal);
        Assert.Contains("ShadowDepth = 0", shadow, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0.55", shadow, StringComparison.Ordinal);
        Assert.Contains("Color = Theme.AccentColor", shadow, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_matches_settings_height_and_typography()
    {
        var title = Between(ReadToolWindow(), "private FrameworkElement BuildTitleBar()", "private FrameworkElement BuildBottomBar()");
        Assert.Contains("Width = 32", title, StringComparison.Ordinal);
        Assert.Contains("Height = 32", title, StringComparison.Ordinal);
        Assert.Contains("FontSize = Design.Type.Title", title, StringComparison.Ordinal);
        Assert.Contains("FontWeight = FontWeights.SemiBold", title, StringComparison.Ordinal);
        Assert.Contains("FontSize = Design.Icon.Inline", title, StringComparison.Ordinal);
        Assert.Contains("Background = Brushes.Transparent", title, StringComparison.Ordinal);
        Assert.Contains("Margin = new Thickness(Design.Gutter, Design.Space.Xl, Design.Gutter, Design.Space.Md)",
            title, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_and_close_motion_are_anchored_to_the_click()
    {
        var source = ReadToolWindow();
        var open = Between(source, "private void StartOpenMotion()", "private void AnchorMotionOrigin()");
        Assert.True(open.IndexOf("AnchorMotionOrigin();", StringComparison.Ordinal) <
                    open.IndexOf("Motion.Open(", StringComparison.Ordinal),
            "the origin must be set before the open storyboard runs");
        Assert.Contains("Motion.Open(", open, StringComparison.Ordinal);

        var origin = Between(source, "private void AnchorMotionOrigin()", "private static ToolContent Placeholder()");
        Assert.Contains("_cursorAnchor", origin, StringComparison.Ordinal);
        Assert.Contains("PointFromScreen", origin, StringComparison.Ordinal);
        Assert.Contains("RenderTransformOrigin", origin, StringComparison.Ordinal);

        var close = Between(source, "protected override void OnClosing", "protected override void OnClosed");
        Assert.Contains("AnchorMotionOrigin();", close, StringComparison.Ordinal);
        Assert.Contains("Motion.Close(", close, StringComparison.Ordinal);
        Assert.Contains("Motion.WindowScaleFrom", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Instant_shell_and_prepare_async_stay_on_the_same_path()
    {
        var source = ReadToolWindow();
        var shell = Between(source, "public ToolWindow(", "private void StartOpenMotion()");
        Assert.Contains("_content = Placeholder();", shell, StringComparison.Ordinal);
        Assert.Contains("Content = Build();", shell, StringComparison.Ordinal);
        Assert.Contains("StartOpenMotion();", shell, StringComparison.Ordinal);
        Assert.Contains("_ = PrepareAsync(animate: true);", shell, StringComparison.Ordinal);
        Assert.Contains("public async Task PrepareAsync(bool animate)", source, StringComparison.Ordinal);
    }

    private static string ReadToolWindow() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "FileAction.App", "UI", "Tools", "ToolWindow.cs"));

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"missing source marker '{start}'");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"missing source marker '{end}' after '{start}'");
        return text[from..to];
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
