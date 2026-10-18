using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.Tests;

public class WheelLayoutTests
{
    private static IReadOnlyList<Operation> ImageConversions() =>
        Catalog.For(Catalog.Build(), WheelKind.Convert, [new FileItem("x.png")], new ToolLocator(Path.GetTempPath()));

    [Fact]
    public void Hidden_slices_are_removed()
    {
        var arranged = WheelLayout.Arrange(ImageConversions(), ["image.to.jpg"], []);
        Assert.DoesNotContain(arranged, o => o.Id == "image.to.jpg");
        Assert.Contains(arranged, o => o.Id == "image.to.webp");
    }

    [Fact]
    public void Favorites_come_first_in_their_order_and_the_rest_keeps_its_order()
    {
        var all = ImageConversions();
        var arranged = WheelLayout.Arrange(all, [], ["image.to.pdf", "image.to.webp"]);
        Assert.Equal("image.to.pdf", arranged[0].Id);
        Assert.Equal("image.to.webp", arranged[1].Id);
        var rest = all.Where(o => o.Id is not "image.to.pdf" and not "image.to.webp").Select(o => o.Id);
        Assert.Equal(rest, arranged.Skip(2).Select(o => o.Id));
    }
}
