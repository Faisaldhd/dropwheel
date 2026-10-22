using System.IO;
using FileAction.App.UI.Tools.Image;
using Xunit;

namespace FileAction.Tests;

public class AtomicWriteTests
{
    [Fact]
    public async Task An_async_producer_is_awaited_before_the_temp_file_is_moved()
    {
        var root = Path.Combine(Path.GetTempPath(), "dw-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await ImageOutput.WriteAtomicFuncAsync(root, "annotated", "png", async target =>
            {
                await Task.Delay(50);
                await File.WriteAllTextAsync(target, "rendered");
            });

            Assert.Equal("rendered", await File.ReadAllTextAsync(result));
            Assert.True(File.Exists(result));
            Assert.Empty(Directory.GetFiles(root, "*.part.*"));
            Assert.Empty(Directory.GetFiles(root, ".*.part.*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
