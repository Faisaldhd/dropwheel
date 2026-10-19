using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// R-16: a failed helper-tool download must describe the tool, the folder and the OS error. The old code
/// reused the output-folder message ("Cannot write to the target folder. Choose another output folder."),
/// which sent the owner to a setting that had nothing to do with the failure.
/// </summary>
public class ToolErrorTests
{
    [Fact]
    public void Failure_names_the_tool_the_folder_and_the_error()
    {
        var exception = new ToolInstallException(
            ExternalTool.Pandoc,
            @"C:\Users\someone\AppData\Local\DropWheel\tools",
            new UnauthorizedAccessException("Access is denied."));

        var text = exception.Describe();

        Assert.Contains("Pandoc", text, StringComparison.Ordinal);
        Assert.Contains(@"DropWheel\tools", text, StringComparison.Ordinal);
        Assert.Contains("access denied", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failure_never_blames_the_output_folder()
    {
        var exception = new ToolInstallException(
            ExternalTool.Ffmpeg,
            @"C:\Temp\DropWheel\tools",
            new IOException("There is not enough space on the disk."));

        var text = exception.Describe();

        Assert.DoesNotContain("output folder", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("target folder", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not enough space", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failure_keeps_the_original_exception_for_the_log()
    {
        var cause = new UnauthorizedAccessException("Access is denied.");
        var exception = new ToolInstallException(ExternalTool.Pandoc, @"C:\x\tools", cause);

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(@"C:\x\tools", exception.Path);
    }
}
