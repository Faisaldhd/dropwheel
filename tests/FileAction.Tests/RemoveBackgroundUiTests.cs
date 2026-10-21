using FileAction.App.UI.Tools;
using FileAction.App.UI.Tools.Image;
using FileAction.App.Wheel;
using FileAction.Core;
using FileAction.Core.Operations;
using ImageMagick;
using ImageMagick.Drawing;

namespace FileAction.Tests;

/// <summary>
/// The Remove BG wiring: the interactive window is registered and reachable, the one-click catalogue slice
/// is a real PNG/alpha operation, and the edge feather the window exposes is part of the core contract.
/// The gate itself is covered by <see cref="ImageToolWindowTests"/> together with the other image views.
/// </summary>
public sealed class RemoveBackgroundUiTests : IDisposable
{
    private readonly string _directory = OutputPaths.TempDirectory();

    public void Dispose() => OutputPaths.TryDelete(_directory);

    [Fact]
    public void The_window_is_registered_and_declares_its_own_title_and_action()
    {
        var photo = new FileItem(Photo("registered.png"));
        var spec = ToolRegistry.For("tools.removebackground", [photo]);

        Assert.NotNull(spec);
        Assert.Equal("tools.removebackground", spec.Id);
        Assert.Equal("tool.removebackground.title", spec.TitleKey);
        Assert.Equal("tool.removebackground.apply", spec.PrimaryLabelKey);
        Assert.True(spec.Applies([photo]));
        Assert.False(spec.Applies([photo, new FileItem(Photo("second.png"))]));
    }

    [Fact]
    public void The_wheel_operation_is_the_interactive_window_and_uses_the_same_id()
    {
        var operation = WheelController.BuildOperations().Single(candidate => candidate.Id == "tools.removebackground");

        Assert.IsAssignableFrom<IInteractiveTool>(operation);
        Assert.Equal(WheelKind.Tools, operation.Wheel);
        Assert.Equal("Remove BG", operation.Label);
        Assert.True(operation.AppliesTo([new FileItem(Photo("wheel.png"))]));
        Assert.IsType<RemoveBackgroundOperation>(operation);
    }

    [Fact]
    public void The_catalogue_slice_is_a_tools_operation_with_an_image_preview()
    {
        var operation = Catalog.Build().Single(candidate => candidate.Id == "image.bg.remove");

        Assert.Equal(WheelKind.Tools, operation.Wheel);
        Assert.Equal(PreviewKind.Image, operation.Preview);
        Assert.Equal("Remove BG", operation.Label);
        Assert.True(operation.AppliesTo([new FileItem(Photo("catalogue.png"))]));
        Assert.False(operation.AppliesTo([new FileItem(_directory)]), "folders are not photos");
    }

    [Fact]
    public async Task The_catalogue_slice_writes_a_transparent_png_and_keeps_the_source()
    {
        var source = Photo("source.png", drawSubject: true);
        var before = File.ReadAllBytes(source);
        var operation = Catalog.Build().Single(candidate => candidate.Id == "image.bg.remove");
        var context = new OperationContext { Tools = new ToolLocator(), OutputDirectory = _directory };

        var result = await operation.ExecuteAsync([new FileItem(source)], context);

        var output = Assert.Single(result.Outputs);
        Assert.Equal("png", Formats.KeyOf(output));
        Assert.True(File.Exists(output));
        Assert.True(before.SequenceEqual(File.ReadAllBytes(source)), "the source image was modified");

        using var cutout = new MagickImage(output);
        Assert.True(cutout.HasAlpha);
        using var pixels = cutout.GetPixels();
        var rgba = pixels.ToByteArray(PixelMapping.RGBA)!;
        Assert.Equal(0, rgba[3]); // the white corner became transparent
        Assert.Equal((255, 255, 255), (rgba[0], rgba[1], rgba[2])); // and its RGB is untouched
        var centre = (20 * 40 + 20) * 4;
        Assert.Equal(255, rgba[centre + 3]); // the subject is still opaque
    }

    private string Photo(string name, bool drawSubject = false)
    {
        var path = Path.Combine(_directory, name);
        using var image = new MagickImage(new MagickColor(255, 255, 255), 40, 40);
        if (drawSubject)
            image.Draw(new Drawables().FillColor(new MagickColor(220, 20, 30)).Rectangle(10, 10, 29, 29));
        image.Write(path, MagickFormat.Png);
        return path;
    }
}

/// <summary>The feather the window exposes maps to the core's 0–100 alpha ramp.</summary>
public class RemoveBackgroundFeatherTests
{
    [Fact]
    public void Feather_is_clamped_to_its_declared_range()
    {
        Assert.Equal(RemoveBackground.MinFeather, RemoveBackground.ClampFeather(-1));
        Assert.Equal(RemoveBackground.DefaultFeather, RemoveBackground.ClampFeather(RemoveBackground.DefaultFeather));
        Assert.Equal(RemoveBackground.MaxFeather, RemoveBackground.ClampFeather(150));
    }

    [Fact]
    public void Feather_zero_is_a_hard_tolerance_cut_and_full_feather_ramps_across_the_band()
    {
        const int fuzz = 40;
        var tolerance = RemoveBackground.Tolerance(fuzz);

        Assert.Equal(0, RemoveBackground.AlphaFor(tolerance * 0.9, fuzz, 0));
        Assert.Equal(255, RemoveBackground.AlphaFor(tolerance, fuzz, 0));

        // At 100 % the ramp spans the whole tolerance band: halfway is halfway between clear and opaque.
        Assert.Equal(128, RemoveBackground.AlphaFor(tolerance / 2, fuzz, 100));
        Assert.Equal(0, RemoveBackground.AlphaFor(0, fuzz, 100));
        Assert.Equal(255, RemoveBackground.AlphaFor(tolerance, fuzz, 100));
    }
}
