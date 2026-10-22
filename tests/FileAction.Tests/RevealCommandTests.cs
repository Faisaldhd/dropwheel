using System.IO;
using FileAction.App.Shell.Protection;
using Xunit;

namespace FileAction.Tests;

public class RevealCommandTests
{
    [Fact]
    public void A_file_is_revealed_selected()
    {
        var root = NewRoot();
        try
        {
            var file = Path.Combine(root, "report.pdf");
            File.WriteAllText(file, "x");

            var (fileName, arguments) = LockOpenWindow.RevealCommand(file);

            Assert.Equal("explorer.exe", fileName);
            Assert.Equal($"/select,\"{file}\"", arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_folder_is_opened()
    {
        var root = NewRoot();
        try
        {
            var (fileName, arguments) = LockOpenWindow.RevealCommand(root);

            Assert.Equal("explorer.exe", fileName);
            Assert.Equal($"\"{root}\"", arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("photo one.pdf")]
    [InlineData("تقرير.pdf")]
    [InlineData("a,b.pdf")]
    public void Spaces_arabic_and_commas_are_quoted(string name)
    {
        var root = NewRoot();
        try
        {
            var file = Path.Combine(root, name);
            File.WriteAllText(file, "x");

            var (_, arguments) = LockOpenWindow.RevealCommand(file);

            Assert.Equal($"/select,\"{file}\"", arguments);
            Assert.StartsWith("/select,\"", arguments);
            Assert.EndsWith("\"", arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("letter.docx", true)]
    [InlineData("photo.PNG", true)]
    [InlineData("notes.txt", true)]
    [InlineData("setup.exe", false)]
    [InlineData("run.bat", false)]
    [InlineData("script.ps1", false)]
    [InlineData("shortcut.lnk", false)]
    [InlineData("invoice.pdf.exe", false)]
    [InlineData("page.url", false)]
    public void Only_documents_and_images_open_automatically(string name, bool safe)
    {
        Assert.Equal(safe, LockOpenWindow.OpensSafely(name));
    }

    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "dw-reveal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
