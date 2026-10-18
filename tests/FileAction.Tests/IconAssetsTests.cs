using System.Security.Cryptography;

namespace FileAction.Tests;

/// <summary>
/// R-14: the icons that ship in the exe and the installer must be the brand icons from <c>branding\</c>.
/// They are kept as two copies (the app embeds <c>Assets\</c>), so a stale copy silently ships the old
/// citrus artwork. These tests pin the SHA-256 of every shipped icon to its brand source.
/// </summary>
public class IconAssetsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Theory]
    [InlineData("DropWheel.ico", "DropWheel.ico")]
    [InlineData("DropWheel-tray-dark.ico", "DropWheel-tray-dark.ico")]
    [InlineData("DropWheel-tray-light.ico", "DropWheel-tray-light.ico")]
    [InlineData("DropWheel.png", "DropWheel-256.png")]
    public void Asset_is_byte_identical_to_the_brand_source(string asset, string brand)
    {
        var root = RepoRoot();
        var shipped = Path.Combine(root, "src", "FileAction.App", "Assets", asset);
        var source = Path.Combine(root, "branding", brand);

        Assert.True(File.Exists(shipped), $"missing shipped icon: {shipped}");
        Assert.True(File.Exists(source), $"missing brand source: {source}");

        Assert.Equal(Sha256(source), Sha256(shipped));
    }

    [Theory]
    [InlineData("DropWheel.ico", 256)]
    [InlineData("DropWheel-tray-dark.ico", 48)]
    [InlineData("DropWheel-tray-light.ico", 48)]
    public void Ico_contains_a_frame_of_each_advertised_size(string name, int largest)
    {
        var path = Path.Combine(RepoRoot(), "src", "FileAction.App", "Assets", name);
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);
        Assert.Equal(0, bytes[3]);

        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var width = bytes[6 + (i * 16)];
            sizes.Add(width == 0 ? 256 : width);
        }

        Assert.Equal(count, sizes.Count);
        Assert.True(count >= 4, $"{name} has only {count} frames");
        Assert.Contains(largest, sizes);
    }
}
