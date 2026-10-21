using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileAction.App.UI;
using FileAction.App.UI.Tools;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The tool window shares Settings' frame geometry and gets its own anchored motion: the look is the
/// 20 DIP Settings card with a 12 DIP content card, the 18 DIP gutter and the dark violet halo, while
/// the open/close pop scales from the pointer that opened the wheel instead of the chrome centre.
/// </summary>
public class ToolWindowChromeTests
{
    /// <summary>The transparent window inset that holds the halo; mirrors FrameMargin in ToolWindow.</summary>
    private const int FrameMargin = 24;

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

    [Fact]
    public void First_visible_frame_is_positioned_before_Show()
    {
        var source = ReadToolWindow();
        var shell = Between(source, "public ToolWindow(", "private void StartOpenMotion()");
        Assert.Contains("_cursorAnchor = CaptureCursor();", shell, StringComparison.Ordinal);
        Assert.Contains("PlaceBeforeShow(initialWork);", shell, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation = WindowStartupLocation.Manual;", shell, StringComparison.Ordinal);

        // SourceInitialized/Loaded placement is the reported bug: Show() paints the default location
        // first and the later SetWindowPos snaps the card across the desktop.
        Assert.DoesNotContain("SourceInitialized +=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceNearCursor", source, StringComparison.Ordinal);

        RunSta(() =>
        {
            var previousMotion = Motion.Override;
            var window = NewWindow();
            try
            {
                Assert.False(double.IsNaN(window.Left), "Left must be set before the first frame");
                Assert.False(double.IsNaN(window.Top), "Top must be set before the first frame");
                var left = window.Left;
                var top = window.Top;

                // Showing an invisible frame proves the first layout pass uses the anchored values and no
                // Loaded/SourceInitialized pass moves the card afterwards.
                Motion.Override = false;
                window.Opacity = 0;
                window.ShowActivated = false;
                window.Show();
                // WPF snaps the requested DIP position to the physical pixel grid; that sub-pixel
                // correction is fine, a Loaded/SourceInitialized placement would move it by much more.
                Assert.True(Math.Abs(window.Left - left) <= 2, $"Left jumped from {left} to {window.Left}");
                Assert.True(Math.Abs(window.Top - top) <= 2, $"Top jumped from {top} to {window.Top}");
            }
            finally
            {
                CloseQuietly(window);
                Motion.Override = previousMotion;
            }
        });
    }

    [Fact]
    public void Halo_has_no_opaque_backdrop_and_the_frame_alone_paints()
    {
        var source = ReadToolWindow();
        Assert.Contains("Background = Brushes.Transparent;", source, StringComparison.Ordinal);
        // Theme.Panel may fill the rounded chrome only; a second page rectangle would square the halo.
        Assert.Equal(1, Count(source, "Background = Theme.Panel"));
        Assert.DoesNotContain("Theme.Page", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Theme.Backdrop", source, StringComparison.Ordinal);

        // The DWM system backdrop painted a page-coloured square across the whole transparent window.
        Assert.DoesNotContain("ApplyBackdrop", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmSetWindowAttribute", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DwmwaSystemBackdropType", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Halo_margin_is_transparent_and_the_shadow_follows_the_rounded_frame(bool dark)
    {
        RunSta(() =>
        {
            var previousTheme = Theme.Override;
            ToolWindow? window = null;
            try
            {
                Theme.Override = dark;
                window = NewWindow();
                var (width, height, pixels) = RenderChrome(window);

                // The 24 DIP halo must be transparent in both themes: an opaque page/panel rectangle or a
                // DWM backdrop would leave the window corners fully opaque.
                foreach (var (x, y) in new[]
                         {
                             (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1),
                             (2, 2), (width - 3, 2), (2, height - 3), (width - 3, height - 3),
                         })
                {
                    Assert.Equal(0, Alpha(pixels, width, x, y));
                }

                // The rounded chrome body is still opaque, so the transparency above is a margin and not
                // a missing frame.
                Assert.True(Alpha(pixels, width, width / 2, height / 2) > 200,
                    $"the frame centre is not painted in {(dark ? "dark" : "light")} mode");

                // The square box around a 20 DIP corner must stay clear; only the shadow may tint it.
                var frameCorner = Alpha(pixels, width, FrameMargin + 2, FrameMargin + 2);
                Assert.True(frameCorner < 250,
                    $"the frame corner is filled ({frameCorner}) in {(dark ? "dark" : "light")} mode");
            }
            finally
            {
                Theme.Override = previousTheme;
                Theme.Refresh();
                CloseQuietly(window);
            }
        });
    }

    private static ToolWindow NewWindow() => new(PlainTool.Instance, [], null);

    private static void CloseQuietly(ToolWindow? window)
    {
        try
        {
            window?.Close();
        }
        catch (InvalidOperationException)
        {
            // A never-shown window can refuse Close; nothing was created to leak.
        }
    }

    private static (int Width, int Height, byte[] Pixels) RenderChrome(ToolWindow window)
    {
        var root = window.DetachForCapture(Brushes.Transparent);
        var width = Math.Max(1, (int)Math.Ceiling(window.Width));
        var height = Math.Max(1, (int)Math.Ceiling(window.Height));
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return (converted.PixelWidth, converted.PixelHeight, pixels);
    }

    private static byte Alpha(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 3];

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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

    /// <summary>A tool that never builds a view: these tests exercise the shared chrome only.</summary>
    private sealed class PlainTool : IToolWindowSpec
    {
        public static readonly PlainTool Instance = new();

        public string Id => "tools.toolwindow-test";

        public string TitleKey => "tool.window.close";

        public ToolSpecLayout Layout => ToolSizes.Default;

        public bool Applies(IReadOnlyList<FileItem> items) => true;

        public ToolContent Create(IReadOnlyList<FileItem> items, DropTargets? targets) =>
            throw new InvalidOperationException("the chrome tests never realize a tool view");
    }
}
