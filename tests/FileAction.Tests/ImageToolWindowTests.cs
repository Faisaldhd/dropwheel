using FileAction.App.UI.Tools;
using FileAction.App.UI.Tools.Image;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Guards the image tool windows: the crop overlay maps to the same rectangle the renderer saves, every
/// image tool gates its primary action on the decoded image, Annotate opens its own window by identity,
/// and Add Background's controls reflow/scroll instead of being clipped in a narrow portrait window.
/// </summary>
public class ImageToolWindowTests
{
    public static TheoryData<string> GatedViews => new()
    {
        "CropTool.cs",
        "AnnotateView.cs",
        Path.Combine("Frame", "AddBackgroundView.cs"),
        "RedactView.cs",
        "EditPhotosView.cs",
        Path.Combine("..", "Collage", "CollageView.cs"),
    };

    [Fact]
    public void Image_gate_grid_is_the_element_the_shared_window_checks()
    {
        Assert.True(typeof(IToolActionGate).IsAssignableFrom(typeof(ImageGateGrid)));
    }

    [Theory]
    [MemberData(nameof(GatedViews))]
    public void Image_tool_content_root_gates_the_primary_action_until_the_image_is_ready(string file)
    {
        var source = ReadImageSource(file);

        // ToolContent.View is the root element, not the view class, so the gate must live on that Grid.
        Assert.Contains("private readonly ImageGateGrid _root = new();", source, StringComparison.Ordinal);
        Assert.Contains("var root = _root;", source, StringComparison.Ordinal);
        Assert.Contains("new ToolContent(root,", source, StringComparison.Ordinal);
        Assert.Contains("_root.Ready();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_action_gate_starts_closed_and_opens_only_once()
    {
        var gate = new ImageActionGate();
        var changes = 0;
        gate.CanApplyChanged += () => changes++;

        Assert.False(gate.CanApply, "Apply must be disabled while the image is still decoding");

        gate.Ready();
        gate.Ready();

        Assert.True(gate.CanApply);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Crop_viewport_fits_the_whole_photo_well_without_the_card_padding()
    {
        // The Image control stretches the bitmap over the full 1000 × 1000 well: 800 × 600 becomes
        // 1000 × 750 at 1.25 with 125 DIP of letterboxing above and below. The old "- 12" fit produced
        // 1.235 and a 119.5 DIP origin, so the frame and the saved pixels disagreed.
        var viewport = CropViewport.Fit(1000, 1000, 800, 600);

        Assert.Equal(1.25, viewport.Scale, 6);
        Assert.Equal(0, viewport.OriginX, 6);
        Assert.Equal(125, viewport.OriginY, 6);
    }

    [Fact]
    public void A_rectangle_drawn_on_the_preview_maps_back_to_the_same_image_pixels()
    {
        var viewport = CropViewport.Fit(640, 480, 800, 600);

        var left = viewport.ToViewX(100);
        var top = viewport.ToViewY(50);
        var right = viewport.ToViewX(300);
        var bottom = viewport.ToViewY(150);

        Assert.Equal(0.8, viewport.Scale, 6);
        Assert.Equal(100, viewport.ToImageX(left), 6);
        Assert.Equal(50, viewport.ToImageY(top), 6);
        Assert.Equal(300, viewport.ToImageX(right), 6);
        Assert.Equal(150, viewport.ToImageY(bottom), 6);
    }

    [Fact]
    public void Crop_layout_uses_the_viewport_helper_and_not_the_old_twelve_dip_inset()
    {
        var source = ReadImageSource("CropTool.cs");

        Assert.Contains("CropViewport.Fit(_preview.ActualWidth, _preview.ActualHeight", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualWidth - 12", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualHeight - 12", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Annotate_operation_opens_its_own_window_by_identity()
    {
        var source = ReadImageSource("AnnotateTool.cs");

        Assert.Contains("new ToolWindow(AnnotateTool.Instance, items, targets).Show();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var spec = ToolRegistry.For", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_background_controls_wrap_and_scroll_in_a_narrow_portrait_window()
    {
        var source = ReadImageSource(Path.Combine("Frame", "AddBackgroundView.cs"));

        var controls = Between(source, "private ScrollViewer BuildControls()", "private FrameworkElement BuildImageRow()");
        Assert.Contains("new ScrollViewer", controls, StringComparison.Ordinal);
        Assert.Contains("StackedRow(\"tool.addbackground.image\"", controls, StringComparison.Ordinal);
        Assert.Contains("StackedRow(\"tool.addbackground.ratio\"", controls, StringComparison.Ordinal);

        var imageRow = Between(source, "private FrameworkElement BuildImageRow()", "private static Grid LabeledRow(");
        Assert.Contains("new WrapPanel", imageRow, StringComparison.Ordinal);

        var sliderRow = Between(source, "private static FrameworkElement SliderRow(", "private void BuildRatios()");
        Assert.Contains("new StackPanel", sliderRow, StringComparison.Ordinal);
        Assert.DoesNotContain("DockPanel", sliderRow, StringComparison.Ordinal);

        var ratios = Between(source, "private void BuildRatios()", "private void PaintRatioPills()");
        Assert.Contains("new WrapPanel", ratios, StringComparison.Ordinal);
    }

    private static string ReadImageSource(string file) => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Image", file));

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
        return directory?.FullName ?? throw new InvalidOperationException("FileAction.sln not found above the test output");
    }
}
